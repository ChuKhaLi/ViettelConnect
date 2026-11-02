namespace ViettelConnect.MySign.Client.Dtos;

/// <summary>Chữ ký thô MySign trả cho mã băm của một tài liệu; ứng dụng tự gắn vào tài liệu.</summary>
/// <remarks>
/// Bản ghi so sánh mảng <c>byte[]</c> theo tham chiếu: hai bản ghi cùng nội dung byte nhưng khác mảng thì <c>==</c>/<c>Equals</c> trả
/// <c>false</c>, và <c>GetHashCode</c> cũng theo tham chiếu. Muốn so nội dung, so từng mảng bằng <c>SequenceEqual</c>.
/// </remarks>
public sealed record DocumentSignatureDto(string DocumentId, byte[] Signature)
{
    /// <summary>Không bao giờ in chữ ký.</summary>
    public override string ToString() => $"DocumentSignatureDto {{ DocumentId = {DocumentId}, Signature = <redacted> }}";
}
