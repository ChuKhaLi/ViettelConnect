namespace ViettelConnect.MySign.Client.Dtos;

/// <summary>Kết quả ký: chứng thư đã dùng và chữ ký theo đúng thứ tự tài liệu của yêu cầu.</summary>
/// <param name="Certificate">Chứng thư đã ký.</param>
/// <param name="Signatures">Một chữ ký cho mỗi tài liệu, cùng thứ tự với yêu cầu.</param>
/// <param name="TransactionId">Mã giao dịch MySign (nếu có) để đối soát.</param>
public sealed record SignHashesResultDto(
    CertificateDto Certificate,
    IReadOnlyList<DocumentSignatureDto> Signatures,
    string? TransactionId)
{
    public override string ToString() =>
        $"SignHashesResultDto {{ CredentialId = {Certificate.CredentialId}, Signatures = {Signatures.Count}, TransactionId = {TransactionId} }}";
}
