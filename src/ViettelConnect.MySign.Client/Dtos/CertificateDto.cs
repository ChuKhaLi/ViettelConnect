namespace ViettelConnect.MySign.Client.Dtos;

/// <summary>Chứng thư số của người ký trên MySign.</summary>
/// <param name="CredentialId">Mã chứng thư trên MySign; truyền vào <see cref="SignHashesRequestDto.CredentialId"/> để chọn đích danh.</param>
/// <param name="SerialNumber">Số hiệu chứng thư (hex), nếu Viettel trả.</param>
/// <param name="SubjectDn">Chủ thể (DN) của chứng thư.</param>
/// <param name="IssuerDn">Tổ chức cấp (DN).</param>
/// <param name="ValidFrom">Hiệu lực từ (UTC).</param>
/// <param name="ValidTo">Hiệu lực đến (UTC).</param>
/// <param name="KeyEnabled">Khoá ký đang bật.</param>
/// <param name="Chain">Chuỗi chứng thư dạng DER thô; chứng thư người ký đứng đầu.</param>
/// <remarks>
/// Bản ghi so sánh các mảng <c>byte[]</c> (và danh sách chứa chúng) theo tham chiếu: hai bản ghi cùng nội dung byte nhưng khác mảng thì <c>==</c>/<c>Equals</c> trả
/// <c>false</c>, và <c>GetHashCode</c> cũng theo tham chiếu. Muốn so nội dung, so từng mảng bằng <c>SequenceEqual</c>.
/// </remarks>
public sealed record CertificateDto(
    string CredentialId,
    string? SerialNumber,
    string? SubjectDn,
    string? IssuerDn,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool KeyEnabled,
    IReadOnlyList<byte[]> Chain)
{
    public override string ToString() =>
        $"CertificateDto {{ CredentialId = {CredentialId}, SerialNumber = {SerialNumber}, ValidTo = {ValidTo:O}, KeyEnabled = {KeyEnabled}, Chain = {Chain.Count} }}";
}
