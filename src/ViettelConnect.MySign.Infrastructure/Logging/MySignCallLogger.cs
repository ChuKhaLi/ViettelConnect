using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Certificates;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Infrastructure.Wire;

namespace ViettelConnect.MySign.Infrastructure.Logging;

/// <summary>
/// Decorator ghi log mỗi lời gọi tới MySign: tên endpoint, thời gian, kết quả, loại lỗi và mã lỗi Viettel.
/// Tuyệt đối không ghi thông tin đăng nhập, token, mã băm, chữ ký hay định danh người ký — các tham số
/// của lời gọi không bao giờ được đưa vào log.
/// </summary>
internal sealed class MySignCallLogger : IMySignWireClient
{
    private readonly IMySignWireClient _inner;
    private readonly ILogger<MySignCallLogger> _logger;
    private readonly ICorrelationIdAccessor _correlation;

    public MySignCallLogger(IMySignWireClient inner, ILogger<MySignCallLogger> logger, ICorrelationIdAccessor correlation)
    {
        _inner = inner;
        _logger = logger;
        _correlation = correlation;
    }

    public Task<AccessToken> LoginAsync(MySignCredentials c, CancellationToken ct) =>
        Wrap("ras/v1/login", () => _inner.LoginAsync(c, ct));

    public Task<IReadOnlyList<Certificate>> GetCertificatesAsync(MySignCredentials c, CancellationToken ct) =>
        Wrap("certificates/info", () => _inner.GetCertificatesAsync(c, ct));

    public Task<SignHashWireResponse> SignHashAsync(MySignCredentials c, SignHashWireRequest r, CancellationToken ct) =>
        Wrap("signHash", () => _inner.SignHashAsync(c, r, ct));

    public Task<StatusSnapshot> GetStatusAsync(MySignCredentials c, string transactionId, CancellationToken ct) =>
        Wrap("requests/status", () => _inner.GetStatusAsync(c, transactionId, ct));

    private async Task<T> Wrap<T>(string endpoint, Func<Task<T>> action)
    {
        var cid = AmbientCorrelation.Resolve(_correlation);
        _logger.LogInformation(
            "MySign call started: endpoint={Endpoint}, correlationId={CorrelationId}",
            endpoint, cid);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action().ConfigureAwait(false);
            sw.Stop();
            _logger.LogInformation(
                "MySign call completed: endpoint={Endpoint}, correlationId={CorrelationId}, durationMs={DurationMs}, outcome={Outcome}",
                endpoint, cid, sw.ElapsedMilliseconds, "Success");
            return result;
        }
        catch (MySignException ex)
        {
            sw.Stop();
            _logger.LogWarning(
                "MySign call completed: endpoint={Endpoint}, correlationId={CorrelationId}, durationMs={DurationMs}, outcome={Outcome}, errorCategory={ErrorCategory}, rawCode={RawCode}",
                endpoint, cid, sw.ElapsedMilliseconds, "Error", ex.Category, ex.RawCode);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(
                "MySign call completed: endpoint={Endpoint}, correlationId={CorrelationId}, durationMs={DurationMs}, outcome={Outcome}, exceptionType={ExceptionType}",
                endpoint, cid, sw.ElapsedMilliseconds, "Error", ex.GetType().Name);
            throw;
        }
    }
}
