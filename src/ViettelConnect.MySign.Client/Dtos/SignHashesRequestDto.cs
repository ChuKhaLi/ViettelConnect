namespace ViettelConnect.MySign.Client.Dtos;

/// <summary>Yêu cầu ký một hoặc nhiều mã băm trong một lần người ký xác nhận.</summary>
/// <param name="Documents">Ít nhất một tài liệu.</param>
/// <param name="Description">Mô tả hiển thị cho người ký (tuỳ chọn).</param>
/// <param name="CredentialId">Chứng thư cần dùng; <c>null</c> thì SDK chọn chứng thư đầu tiên còn hiệu lực.</param>
public sealed record SignHashesRequestDto(
    IReadOnlyList<HashDocumentDto> Documents,
    string? Description = null,
    string? CredentialId = null);
