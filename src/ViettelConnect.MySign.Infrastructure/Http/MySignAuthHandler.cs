using System.Net;
using System.Net.Http.Headers;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Infrastructure.Wire;

namespace ViettelConnect.MySign.Infrastructure.Http;

/// <summary>
/// Gắn <c>Authorization: Bearer</c> cho mọi request trừ login. Token lấy từ <see cref="ITokenCache"/> theo
/// <see cref="MySignCredentials.CacheKey"/> của chính request (đọc từ <see cref="HttpRequestMessage.Options"/>),
/// hoặc đăng nhập qua <see cref="IMySignWireClient.LoginAsync"/> khi chưa có/đã sắp hết hạn. Việc lấy token là
/// single-flight theo khoá nên hai đơn vị không chặn hay dùng chung token của nhau. Gặp 401: xoá token, đăng nhập
/// lại đúng một lần (chia sẻ giữa các request đồng thời), gửi lại một lần; 401 lần hai thì ném
/// <see cref="MySignErrorCategory.Authentication"/>. Wire client được lấy qua <c>Func</c> để tránh vòng phụ thuộc
/// handler -> wire client -> HttpClient -> handler. Lỗi do handler ném (và lượt login nó khởi phát) mang mã tương quan
/// của chính request (<see cref="MySignRequestOptions.CorrelationId"/>), không phải của scope handler.
/// </summary>
internal sealed class MySignAuthHandler : DelegatingHandler
{
    /// <summary>Token coi như hết hạn sớm chừng này, vì wire client trả đúng hạn của Viettel (không có biên).</summary>
    internal static readonly TimeSpan TokenExpiryMargin = TimeSpan.FromSeconds(60);

    private readonly Func<IMySignWireClient> _wireFactory;
    private readonly ITokenCache _cache;
    private readonly SingleFlightRefresh _singleFlight;
    private readonly TimeProvider _time;
    private readonly ICorrelationIdAccessor _correlation;

    public MySignAuthHandler(
        Func<IMySignWireClient> wireFactory,
        ITokenCache cache,
        SingleFlightRefresh singleFlight,
        TimeProvider time,
        ICorrelationIdAccessor correlation)
    {
        _wireFactory = wireFactory;
        _cache = cache;
        _singleFlight = singleFlight;
        _time = time;
        _correlation = correlation;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(MySignRequestOptions.SkipAuth, out var skip) && skip)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (!request.Options.TryGetValue(MySignRequestOptions.Credentials, out var credentials) || credentials is null)
        {
            throw new InvalidOperationException("MySign request has no credentials attached; cannot choose a Bearer token.");
        }

        var correlationId = CorrelationIdOf(request);
        var body = await RequestCloner.BufferContentAsync(request, cancellationToken).ConfigureAwait(false);

        var token = await GetTokenAsync(credentials, staleValue: null, correlationId, cancellationToken).ConfigureAwait(false);
        using (var first = RequestCloner.Clone(request, body, skipAuthorizationHeader: true))
        {
            first.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            var response = await base.SendAsync(first, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return response;
            }

            response.Dispose();
        }

        // 401: token bị từ chối. Lượt làm mới chỉ đăng nhập lại nếu cache vẫn giữ đúng token vừa bị từ chối,
        // để các request 401 đồng thời (hoặc đến muộn) dùng lại token mới thay vì đăng nhập thêm.
        var refreshed = await GetTokenAsync(credentials, staleValue: token.Value, correlationId, cancellationToken).ConfigureAwait(false);
        using var retry = RequestCloner.Clone(request, body, skipAuthorizationHeader: true);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.Value);
        var retryResponse = await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            retryResponse.Dispose();
            await _cache.RemoveAsync(credentials.CacheKey, cancellationToken).ConfigureAwait(false);
            throw new MySignException(
                MySignErrorCategory.Authentication,
                "401",
                "MySign rejected the request with 401 even after a fresh access token.",
                correlationId);
        }

        return retryResponse;
    }

    private string CorrelationIdOf(HttpRequestMessage request) =>
        request.Options.TryGetValue(MySignRequestOptions.CorrelationId, out var id) && !string.IsNullOrWhiteSpace(id)
            ? id
            : _correlation.Current;

    private async Task<AccessToken> GetTokenAsync(
        MySignCredentials credentials,
        string? staleValue,
        string correlationId,
        CancellationToken ct)
    {
        var key = credentials.CacheKey;
        if (staleValue is null)
        {
            var cached = await _cache.TryGetAsync(key, ct).ConfigureAwait(false);
            if (cached is not null && IsFresh(cached))
            {
                return cached;
            }
        }

        return await _singleFlight.RefreshAsync(
            key,
            async _ =>
            {
                var current = await _cache.TryGetAsync(key, CancellationToken.None).ConfigureAwait(false);
                if (current is not null && IsFresh(current) && !string.Equals(current.Value, staleValue, StringComparison.Ordinal))
                {
                    return current;
                }

                await _cache.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);

                // Wire client lấy từ scope của handler: mượn mã của request gốc để login ghi log/ném lỗi đúng mã.
                // (Lượt login dùng chung giữa các request cùng khoá mang mã của request khởi phát nó.)
                AccessToken fresh;
                using (AmbientCorrelation.Use(correlationId))
                {
                    fresh = await _wireFactory().LoginAsync(credentials, CancellationToken.None).ConfigureAwait(false);
                }

                await _cache.SetAsync(key, fresh, CancellationToken.None).ConfigureAwait(false);
                return fresh;
            },
            ct).ConfigureAwait(false);
    }

    private bool IsFresh(AccessToken token) => token.ExpiresAtUtc - TokenExpiryMargin > _time.GetUtcNow();
}
