using System.Collections.Concurrent;
using ViettelConnect.MySign.Application.Abstractions;

namespace ViettelConnect.MySign.Infrastructure.Callback;

/// <summary>Kho giao dịch chờ trong bộ nhớ, an toàn đa luồng, khoá theo (tenantKey, transactionId).</summary>
internal sealed class InMemoryPendingSignatureStore : IPendingSignatureStore
{
    private readonly ConcurrentDictionary<(string TenantKey, string TransactionId), PendingSignature> _items = new();

    public ValueTask SaveAsync(PendingSignature p, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(p);
        _items[(p.TenantKey, p.TransactionId)] = p;
        return ValueTask.CompletedTask;
    }

    public ValueTask<PendingSignature?> FindAsync(string tenantKey, string transactionId, CancellationToken ct) =>
        ValueTask.FromResult(
            _items.TryGetValue((tenantKey, transactionId), out var p) ? p : null);

    public ValueTask<bool> TryRemoveAsync(string tenantKey, string transactionId, CancellationToken ct) =>
        ValueTask.FromResult(_items.TryRemove((tenantKey, transactionId), out _));
}
