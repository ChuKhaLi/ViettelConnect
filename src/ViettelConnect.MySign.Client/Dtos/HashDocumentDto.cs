namespace ViettelConnect.MySign.Client.Dtos;

/// <summary>Một tài liệu cần ký. Ứng dụng tự băm tài liệu; SDK chỉ gửi mã băm.</summary>
/// <param name="DocumentId">Mã do ứng dụng đặt, không trùng trong một yêu cầu; dùng để ghép chữ ký trả về. Không bao giờ được gửi làm <c>document_id</c> (SDK đánh số theo thứ tự trong yêu cầu và ánh xạ ngược); chỉ được dùng, sau khi chuẩn hoá và base64, làm nhãn hiển thị dự phòng khi <c>DisplayName</c> rỗng sau khi chuẩn hoá.</param>
/// <param name="DisplayName">Tên hiển thị cho người ký trên ứng dụng MySign (có dấu được; SDK tự chuẩn hoá).</param>
/// <param name="Sha256">Mã băm SHA-256 thô, đúng 32 byte.</param>
/// <remarks>
/// Bản ghi so sánh mảng <c>byte[]</c> theo tham chiếu: hai bản ghi cùng nội dung byte nhưng khác mảng thì <c>==</c>/<c>Equals</c> trả
/// <c>false</c>, và <c>GetHashCode</c> cũng theo tham chiếu. Muốn so nội dung, so từng mảng bằng <c>SequenceEqual</c>.
/// </remarks>
public sealed record HashDocumentDto(string DocumentId, string DisplayName, byte[] Sha256)
{
    public override string ToString() => $"HashDocumentDto {{ DocumentId = {DocumentId}, DisplayName = {DisplayName} }}";
}
