using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Application.Signing;
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
    private const int Sha256Length = 32;
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
        Validate(request);

        var polling = _settings.SignMode == SignMode.Polling;

        var who = _credentials.Get();
        var certificates = await _wire.GetCertificatesAsync(who, ct).ConfigureAwait(false);

        Certificate certificate;
        if (request.CredentialId is { } credentialId)
        {
            certificate = certificates.FirstOrDefault(c => string.Equals(c.CredentialId, credentialId, StringComparison.Ordinal))
                ?? throw Fail(MySignErrorCategory.NoActiveCertificate, "Chứng thư được yêu cầu không thuộc người ký này.");
        }
        else
        {
            certificate = await _selector.SelectAsync(certificates, ct).ConfigureAwait(false);
        }

        var sad = await _authorization.GetSadAsync(who, certificate, ct).ConfigureAwait(false);
        var wireRequest = new SignHashWireRequest(
            certificate.CredentialId,
            request.Documents
                .Select(d => (d.DocumentId, DisplayLabelEncoder.Encode(d.DisplayName, d.DocumentId), d.Sha256))
                .ToArray(),
            string.IsNullOrWhiteSpace(request.Description) ? null : DisplayLabelEncoder.Encode(request.Description, "Document"),
            polling ? _settings.PollingAsyncCode : SynchronousAsyncCode,
            sad);

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
                $"MySign trả {wireSignatures?.Count ?? 0} chữ ký cho {request.Documents.Count} mã băm.");
        }

        var signatures = new DocumentSignature[request.Documents.Count];
        for (var i = 0; i < signatures.Length; i++)
        {
            signatures[i] = new DocumentSignature(request.Documents[i].DocumentId, wireSignatures[i]);
        }

        return new SignHashesResult(certificate, signatures, transactionId);
    }

    private void Validate(SignHashesRequest request)
    {
        if (request.Documents is null || request.Documents.Count == 0)
        {
            throw Fail(MySignErrorCategory.Validation, "Cần ít nhất một tài liệu để ký.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < request.Documents.Count; i++)
        {
            var doc = request.Documents[i];
            if (doc is null || string.IsNullOrWhiteSpace(doc.DocumentId))
            {
                throw Fail(MySignErrorCategory.Validation, $"Tài liệu thứ {i + 1} thiếu DocumentId.");
            }

            if (doc.Sha256 is null || doc.Sha256.Length != Sha256Length)
            {
                throw Fail(MySignErrorCategory.Validation, $"Mã băm của tài liệu thứ {i + 1} phải đúng {Sha256Length} byte (SHA-256).");
            }

            if (!seen.Add(doc.DocumentId))
            {
                throw Fail(MySignErrorCategory.Validation, $"DocumentId của tài liệu thứ {i + 1} bị trùng.");
            }
        }
    }

    private MySignException Fail(MySignErrorCategory category, string detail) =>
        new(category, rawCode: null, detail, _correlation.Current);
}
