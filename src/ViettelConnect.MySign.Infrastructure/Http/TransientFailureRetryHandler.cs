using System.Globalization;
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
/// cho thời gian lùi tính toán. Người gọi huỷ thì ném lại nguyên trạng. Hết số lần thì ném
/// <see cref="MySignException"/> loại <see cref="MySignErrorCategory.Transport"/>. Mỗi lần gửi dùng bản sao
/// của request (thân được đệm) nên gửi lại được.
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

        var statusCode = lastResponse?.StatusCode;
        lastResponse?.Dispose();
        throw new MySignException(
            MySignErrorCategory.Transport,
            statusCode is null ? null : ((int)statusCode.Value).ToString(CultureInfo.InvariantCulture),
            BuildExhaustedDetail(statusCode, lastException, maxAttempts),
            _correlation.Current,
            lastException);
    }

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

    private static string BuildExhaustedDetail(HttpStatusCode? lastStatus, Exception? lastException, int attempts)
    {
        if (lastStatus is not null)
        {
            return $"MySign transport retries exhausted (attempts={attempts}, lastStatus={(int)lastStatus.Value}).";
        }

        if (lastException is not null)
        {
            return $"MySign transport retries exhausted (attempts={attempts}, lastException={lastException.GetType().Name}).";
        }

        return $"MySign transport retries exhausted (attempts={attempts}).";
    }
}
