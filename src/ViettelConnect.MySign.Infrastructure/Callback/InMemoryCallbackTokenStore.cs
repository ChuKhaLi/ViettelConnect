using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ViettelConnect.MySign.Application.Abstractions;

namespace ViettelConnect.MySign.Infrastructure.Callback;

/// <summary>
/// Kho token callback trong bộ nhớ, an toàn đa luồng. Token là 32 byte ngẫu nhiên mật mã (base64url);
/// kho chỉ giữ băm SHA-256 của token nên không thể đọc lại token từ kho.
/// </summary>
internal sealed class InMemoryCallbackTokenStore : ICallbackTokenStore
{
    private const int TokenBytes = 32;
    private const int SweepEvery = 64;

    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, (string TenantKey, DateTimeOffset ExpiresAt)> _tokens = new(StringComparer.Ordinal);
    private int _issued;

    public InMemoryCallbackTokenStore(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public ValueTask<string> IssueAsync(string tenantKey, TimeSpan lifetime, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantKey);
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Lifetime must be > 0.");
        }

        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = _time.GetUtcNow();
        _tokens[Key(token)] = (tenantKey, now + lifetime);

        if (Interlocked.Increment(ref _issued) % SweepEvery == 0)
        {
            Sweep(now);
        }

        return ValueTask.FromResult(token);
    }

    public ValueTask<string?> ValidateAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var key = Key(token);
        if (!_tokens.TryGetValue(key, out var entry))
        {
            return ValueTask.FromResult<string?>(null);
        }

        if (_time.GetUtcNow() > entry.ExpiresAt)
        {
            _tokens.TryRemove(key, out _);
            return ValueTask.FromResult<string?>(null);
        }

        return ValueTask.FromResult<string?>(entry.TenantKey);
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var pair in _tokens)
        {
            if (now > pair.Value.ExpiresAt)
            {
                _tokens.TryRemove(pair.Key, out _);
            }
        }
    }

    private static string Key(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
