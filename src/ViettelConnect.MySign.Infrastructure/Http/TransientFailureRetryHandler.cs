using System.Net;
using Microsoft.Extensions.Options;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Infrastructure.Configuration;
using ViettelConnect.MySign.Infrastructure.Wire;

namespace ViettelConnect.MySign.Infrastructure.Http;

/// <summary>
/// Thử lại có giới hạn, lùi mũ kèm jitter đầy đủ. Thử lại 5xx, 429, <see cref="HttpRequestException"/> và hết
/// thời gian chờ của thao tác. Với 429 có <c>Retry-After</c>, giá trị header (chặn trên bởi <c>MaxDelay</c>) thay
/// cho thời gian lùi tính toán. Người gọi huỷ thì ném lại nguyên trạng. Hết số lần: nếu lần cuối là phản hồi
/// 5xx/429 thì trả phản hồi đó (wire client phân loại theo thân lỗi Viettel); nếu là lỗi mạng/hết giờ thì ném
/// <see cref="MySignException"/> loại <see cref="MySignErrorCategory.Transport"/> mang mã tương quan của chính request
/// (<see cref="MySignRequestOptions.CorrelationId"/>). Mỗi lần gửi dùng bản sao của request (thân được đệm) nên gửi lại được.
/// </summary>
internal sealed class TransientFailureRetryHandler : DelegatingHandler
{
    private readonly IOptions<MySignOptions> _options;
    private readonly IDelayer _delayer;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly TimeProvider _time;
    private readonly Func<int, int> _randomBudget;

    public TransientFailureRetryHandler(
        IOptions<MySignOptions> options,
        IDelayer delayer,
        ICorrelationIdAccessor correlation,
        TimeProvider time)
        : this(options, delayer, correlation, time, max => Random.Shared.Next(0, Math.Max(1, max)))
    {
    }

    internal TransientFailureRetryHandler(
        IOptions<MySignOptions> options,
        IDelayer delayer,
        ICorrelationIdAccessor correlation,
        TimeProvider time,
        Func<int, int> randomBudget)
    {
        _options = options;
        _delayer = delayer;
        _correlation = correlation;
        _time = time;
        _randomBudget = randomBudget;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var retry = _options.Value.TransportRetry;
        // Request không idempotent (signHash) chỉ gửi đúng một lần.
        var maxAttempts = request.Options.TryGetValue(MySignRequestOptions.NoRetry, out var noRetry) && noRetry
            ? 1
            : Math.Max(1, retry.MaxAttempts);
        var baseDelayMs = (int)retry.BaseDelay.TotalMilliseconds;
        var maxDelayMs = (int)retry.MaxDelay.TotalMilliseconds;
        var body = await RequestCloner.BufferContentAsync(request, cancellationToken).ConfigureAwait(false);

        HttpResponseMessage? lastResponse = null;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                lastResponse?.Dispose();
                lastResponse = null;
                lastException = null;

                using var clone = RequestCloner.Clone(request, body);
                var resp = await base.SendAsync(clone, cancellationToken).ConfigureAwait(false);
                if (!IsTransient(resp.StatusCode))
                {
                    return resp;
                }

                lastResponse = resp;
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
            }

            if (attempt == maxAttempts)
            {
                break;
            }

            var delayMs = ComputeBackoffMs(attempt, baseDelayMs, maxDelayMs, lastResponse);
            await _delayer.DelayAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken).ConfigureAwait(false);
        }

        // Lần thử cuối vẫn 5xx/429: trả nguyên phản hồi để wire client phân loại theo mã lỗi Viettel trong thân
        // (vd signHash 500 {"error":"58083"} là SignFailed); không có mã nhận diện được thì ErrorMapper ra Transport.
        if (lastResponse is not null)
        {
            return lastResponse;
        }

        throw new MySignException(
            MySignErrorCategory.Transport,
            null,
            BuildExhaustedDetail(lastException, maxAttempts),
            CorrelationIdOf(request),
            lastException);
    }

    private string CorrelationIdOf(HttpRequestMessage request) =>
        request.Options.TryGetValue(MySignRequestOptions.CorrelationId, out var id) && !string.IsNullOrWhiteSpace(id)
            ? id
            : _correlation.Current;

    private int ComputeBackoffMs(int attempt, int baseDelayMs, int maxDelayMs, HttpResponseMessage? response)
    {
        if (response is not null && response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter is not null)
            {
                if (retryAfter.Delta is { } delta)
                {
                    return (int)Math.Min(delta.TotalMilliseconds, maxDelayMs);
                }

                if (retryAfter.Date is { } date)
                {
                    var ms = (date.ToUniversalTime() - _time.GetUtcNow()).TotalMilliseconds;
                    if (ms > 0)
                    {
                        return (int)Math.Min(ms, maxDelayMs);
                    }
                }
            }
        }

        var capped = Math.Min(maxDelayMs, baseDelayMs * (1 << Math.Min(attempt - 1, 16)));
        return _randomBudget(Math.Max(1, capped));
    }

    private static bool IsTransient(HttpStatusCode code) =>
        code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    private static string BuildExhaustedDetail(Exception? lastException, int attempts) =>
        lastException is not null
            ? $"MySign transport retries exhausted (attempts={attempts}, lastException={lastException.GetType().Name})."
            : $"MySign transport retries exhausted (attempts={attempts}).";
}
