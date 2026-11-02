using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Errors;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>
/// Bắt đầu ký bất đồng bộ có callback (<c>async = 3</c>): gửi signHash rồi lưu giao dịch chờ theo đơn vị.
/// Kết quả đến sau qua <see cref="HandleCallbackResult"/>. signHash không được tự thử lại (xem WireClient).
/// </summary>
internal sealed class BeginSignHashes
{
    private const int CallbackAsyncCode = 3;

    private readonly IMySignWireClient _wire;
    private readonly IMySignCredentialsAccessor _credentials;
    private readonly ICertificateSelector _selector;
    private readonly ISignAuthorizationProvider _authorization;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly IPendingSignatureStore _pending;
    private readonly TimeProvider _time;

    public BeginSignHashes(
        IMySignWireClient wire,
        IMySignCredentialsAccessor credentials,
        ICertificateSelector selector,
        ISignAuthorizationProvider authorization,
        ICorrelationIdAccessor correlation,
        IPendingSignatureStore pending,
        TimeProvider time)
    {
        _wire = wire ?? throw new ArgumentNullException(nameof(wire));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task<BeginSignResult> ExecuteAsync(SignHashesRequest request, string tenantKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        SignHashesSupport.Validate(request, _correlation);
        if (string.IsNullOrWhiteSpace(tenantKey))
        {
            throw SignHashesSupport.Fail(_correlation, MySignErrorCategory.Validation, "Thiếu mã đơn vị (tenantKey) cho yêu cầu ký callback.");
        }

        var who = _credentials.Get();
        var certificate = await SignHashesSupport
            .SelectCertificateAsync(_wire, who, request.CredentialId, _selector, _correlation, ct).ConfigureAwait(false);
        var sad = await _authorization.GetSadAsync(who, certificate, ct).ConfigureAwait(false);
        var wireRequest = SignHashesSupport.BuildWireRequest(request, certificate, CallbackAsyncCode, sad);

        var response = await _wire.SignHashAsync(who, wireRequest, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response.TransactionId))
        {
            throw SignHashesSupport.Fail(
                _correlation, MySignErrorCategory.ProtocolViolation, "MySign không trả mã giao dịch cho yêu cầu ký bất đồng bộ.");
        }

        await _pending.SaveAsync(
            new PendingSignature(
                response.TransactionId,
                tenantKey,
                certificate,
                request.Documents.Select(d => d.DocumentId).ToArray(),
                _time.GetUtcNow()),
            ct).ConfigureAwait(false);

        return new BeginSignResult(response.TransactionId, certificate);
    }
}
