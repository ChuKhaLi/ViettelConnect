using System.Collections.Concurrent;
using ViettelConnect.MySign.Application.Abstractions;

namespace ViettelConnect.MySign.Infrastructure.Callback;

/// <summary>
/// Kho giao dịch chờ trong bộ nhớ, an toàn đa luồng, khoá theo (tenantKey, transactionId). Giao dịch không bao giờ
/// được Viettel báo kết quả không nằm mãi trong bộ nhớ: mục lưu lâu hơn <c>maxAge</c> (mặc định 24 giờ, theo đồng hồ
/// của kho lúc lưu) bị coi như không tồn tại khi truy cập và bị quét bỏ định kỳ khi lưu mục mới.
/// </summary>
internal sealed class InMemoryPendingSignatureStore : IPendingSignatureStore
{
    internal static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(24);

    private const int SweepEvery = 64;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time;
    private readonly TimeSpan _maxAge;
    private readonly ConcurrentDictionary<(string TenantKey, string TransactionId), (PendingSignature Item, DateTimeOffset StoredAt)> _items = new();
    private int _saved;
    private long _lastSweepTicks;

    public InMemoryPendingSignatureStore()
        : this(TimeProvider.System, DefaultMaxAge)
    {
    }

    public InMemoryPendingSignatureStore(TimeProvider time, TimeSpan maxAge)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        if (maxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), "Max age must be > 0.");
        }

        _maxAge = maxAge;
        _lastSweepTicks = _time.GetUtcNow().UtcTicks;
    }

    /// <summary>Số mục đang giữ (kể cả mục đã quá hạn nhưng chưa bị quét) — cho test.</summary>
    internal int Count => _items.Count;

    public ValueTask SaveAsync(PendingSignature p, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(p);
        var now = _time.GetUtcNow();
        _items[(p.TenantKey, p.TransactionId)] = (p, now);

        var dueByCount = Interlocked.Increment(ref _saved) % SweepEvery == 0;
        var dueByTime = now.UtcTicks - Interlocked.Read(ref _lastSweepTicks) >= SweepInterval.Ticks;
        if (dueByCount || dueByTime)
        {
            Interlocked.Exchange(ref _lastSweepTicks, now.UtcTicks);
            Sweep(now);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<PendingSignature?> FindAsync(string tenantKey, string transactionId, CancellationToken ct)
    {
        var key = (tenantKey, transactionId);
        if (!_items.TryGetValue(key, out var entry))
        {
            return ValueTask.FromResult<PendingSignature?>(null);
        }

        if (IsExpired(entry.StoredAt, _time.GetUtcNow()))
        {
            _items.TryRemove(new KeyValuePair<(string, string), (PendingSignature, DateTimeOffset)>(key, entry));
            return ValueTask.FromResult<PendingSignature?>(null);
        }

        return ValueTask.FromResult<PendingSignature?>(entry.Item);
    }

    public ValueTask<bool> TryRemoveAsync(string tenantKey, string transactionId, CancellationToken ct)
    {
        if (!_items.TryRemove((tenantKey, transactionId), out var entry))
        {
            return ValueTask.FromResult(false);
        }

        // Mục đã quá hạn được dọn nhưng không tính là "thắng" (giống như nó đã không còn tồn tại).
        return ValueTask.FromResult(!IsExpired(entry.StoredAt, _time.GetUtcNow()));
    }

    private bool IsExpired(DateTimeOffset storedAt, DateTimeOffset now) => now - storedAt > _maxAge;

    private void Sweep(DateTimeOffset now)
    {
        foreach (var pair in _items)
        {
            if (IsExpired(pair.Value.StoredAt, now))
            {
                _items.TryRemove(pair);
            }
        }
    }
}
