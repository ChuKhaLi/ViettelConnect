namespace ViettelConnect.MySign.Client.Dtos;

/// <summary>Một tài liệu cần ký. Ứng dụng tự băm tài liệu; SDK chỉ gửi mã băm.</summary>
/// <param name="DocumentId">Mã do ứng dụng đặt, không trùng trong một yêu cầu; dùng để ghép chữ ký trả về.</param>
/// <param name="DisplayName">Tên hiển thị cho người ký trên ứng dụng MySign (có dấu được; SDK tự chuẩn hoá).</param>
/// <param name="Sha256">Mã băm SHA-256 thô, đúng 32 byte.</param>
public sealed record HashDocumentDto(string DocumentId, string DisplayName, byte[] Sha256)
{
    public override string ToString() => $"HashDocumentDto {{ DocumentId = {DocumentId}, DisplayName = {DisplayName} }}";
}
