namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>Một tài liệu cần ký: mã định danh của bên gọi, tên hiển thị cho người ký và mã băm SHA256 (32 byte).</summary>
public sealed record HashDocument(string DocumentId, string DisplayName, byte[] Sha256);

/// <summary>Yêu cầu ký một hoặc nhiều mã băm trong một lần người ký xác nhận.</summary>
/// <param name="Documents">Ít nhất một tài liệu; <c>DocumentId</c> không trùng nhau.</param>
/// <param name="Description">Mô tả hiển thị cho người ký (tuỳ chọn).</param>
/// <param name="CredentialId">Chứng thư cần dùng; <c>null</c> thì để bộ chọn chứng thư quyết định.</param>
public sealed record SignHashesRequest(IReadOnlyList<HashDocument> Documents, string? Description, string? CredentialId);
