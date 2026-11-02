using ViettelConnect.MySign.Application.UseCases;
using ViettelConnect.MySign.Client.Dtos;
using ViettelConnect.MySign.Domain.Certificates;
using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Client.Mapping;

/// <summary>Ánh xạ giữa DTO công khai và kiểu tầng Application/Domain. OID và chi tiết wire bị bỏ lại ở đây.</summary>
internal static class DtoMapper
{
    public static SignHashesRequest ToRequest(SignHashesRequestDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Phần tử null giữ nguyên vị trí để use case báo lỗi kiểm tra đúng "tài liệu thứ N".
        var documents = dto.Documents?
            .Select(d => d is null ? null! : new HashDocument(d.DocumentId, d.DisplayName, d.Sha256))
            .ToArray();
        return new SignHashesRequest(documents!, dto.Description, dto.CredentialId);
    }

    public static CertificateDto ToDto(Certificate c) =>
        new(c.CredentialId, c.SerialNumber, c.SubjectDn, c.IssuerDn, c.ValidFrom, c.ValidTo, c.KeyEnabled, c.Chain);

    public static IReadOnlyList<CertificateDto> ToDto(IReadOnlyList<Certificate> certificates) =>
        certificates.Select(ToDto).ToArray();

    public static SignHashesResultDto ToDto(SignHashesResult r) =>
        new(ToDto(r.Certificate), ToDto(r.Signatures), r.TransactionId);

    public static BeginSignResultDto ToDto(BeginSignResult r) =>
        new(r.TransactionId, ToDto(r.Certificate));

    public static CallbackResponseDto ToDto(CallbackResponse r) =>
        new(r.HttpStatus, r.JsonBody);

    public static CallbackSignOutcomeDto ToDto(CallbackSignOutcome o) =>
        new(o.TenantKey, o.TransactionId, ToDto(o.Status), ToDto(o.Signatures), o.RawStatus);

    public static CallbackSignStatus ToDto(SignStatus s) => s switch
    {
        SignStatus.Pending => CallbackSignStatus.Pending,
        SignStatus.Succeeded => CallbackSignStatus.Succeeded,
        SignStatus.Rejected => CallbackSignStatus.Rejected,
        SignStatus.TimedOut => CallbackSignStatus.TimedOut,
        SignStatus.Failed => CallbackSignStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unknown sign status."),
    };

    private static IReadOnlyList<DocumentSignatureDto> ToDto(IReadOnlyList<DocumentSignature> signatures) =>
        signatures.Select(s => new DocumentSignatureDto(s.DocumentId, s.Signature)).ToArray();
}
