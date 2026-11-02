using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Certificates;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>
/// Ký mã băm SHA256 qua MySign: nhánh đồng bộ (một lệnh signHash, người ký xác nhận trên ứng dụng MySign)
/// hoặc nhánh hỏi trạng thái (signHash trả transactionId rồi hỏi requests/status có giới hạn thời gian).
/// </summary>
internal sealed class SignHashes
{
    private const int SynchronousAsyncCode = 0;

    private readonly IMySignWireClient _wire;
    private readonly IMySignCredentialsAccessor _credentials;
    private readonly ICertificateSelector _selector;
    private readonly ISignAuthorizationProvider _authorization;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly PollSignStatus _poll;
    private readonly MySignSigningSettings _settings;

    public SignHashes(
        IMySignWireClient wire,
        IMySignCredentialsAccessor credentials,
        ICertificateSelector selector,
        ISignAuthorizationProvider authorization,
        ICorrelationIdAccessor correlation,
        PollSignStatus poll,
        MySignSigningSettings settings)
    {
        _wire = wire ?? throw new ArgumentNullException(nameof(wire));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _poll = poll ?? throw new ArgumentNullException(nameof(poll));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<SignHashesResult> ExecuteAsync(SignHashesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        SignHashesSupport.Validate(request, _correlation);

        var polling = _settings.SignMode == SignMode.Polling;

        var who = _credentials.Get();
        var certificate = await SignHashesSupport
            .SelectCertificateAsync(_wire, who, request.CredentialId, _selector, _correlation, ct).ConfigureAwait(false);

        var sad = await _authorization.GetSadAsync(who, certificate, ct).ConfigureAwait(false);
        var wireRequest = SignHashesSupport.BuildWireRequest(
            request, certificate, polling ? _settings.PollingAsyncCode : SynchronousAsyncCode, sad);

        var response = await _wire.SignHashAsync(who, wireRequest, ct).ConfigureAwait(false);

        if (!polling)
        {
            return BuildResult(request, certificate, response.Signatures, response.TransactionId);
        }

        if (string.IsNullOrWhiteSpace(response.TransactionId))
        {
            throw Fail(MySignErrorCategory.ProtocolViolation, "MySign không trả mã giao dịch cho yêu cầu ký bất đồng bộ.");
        }

        var polled = await _poll.ExecuteAsync(
            who, response.TransactionId, _settings.PollingInterval, _settings.PollingTotalTimeout, ct).ConfigureAwait(false);
        return BuildResult(request, certificate, polled, response.TransactionId);
    }

    private SignHashesResult BuildResult(
        SignHashesRequest request,
        Certificate certificate,
        IReadOnlyList<byte[]>? wireSignatures,
        string? transactionId)
    {
        // Không bao giờ trả kết quả thiếu hoặc lệch thứ tự: ghép theo vị trí nên số lượng phải khớp tuyệt đối.
        if (wireSignatures is null || wireSignatures.Count != request.Documents.Count)
        {
            throw Fail(
                MySignErrorCategory.ProtocolViolation,
                $"MySign trả {wireSignatures?.Count ?? 0} chữ ký cho {request.Documents.Count} mã băm.",
                transactionId);
        }

        var signatures = new DocumentSignature[request.Documents.Count];
        for (var i = 0; i < signatures.Length; i++)
        {
            signatures[i] = new DocumentSignature(request.Documents[i].DocumentId, wireSignatures[i]);
        }

        return new SignHashesResult(certificate, signatures, transactionId);
    }

    private MySignException Fail(MySignErrorCategory category, string detail, string? transactionId = null) =>
        new(category, rawCode: null, detail, _correlation.Current, transactionId: transactionId);
}
