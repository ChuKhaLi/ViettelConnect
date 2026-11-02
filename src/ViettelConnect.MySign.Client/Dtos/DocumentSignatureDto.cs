namespace ViettelConnect.MySign.Client.Dtos;

/// <summary>Chữ ký thô MySign trả cho mã băm của một tài liệu; ứng dụng tự gắn vào tài liệu.</summary>
public sealed record DocumentSignatureDto(string DocumentId, byte[] Signature)
{
    /// <summary>Không bao giờ in chữ ký.</summary>
    public override string ToString() => $"DocumentSignatureDto {{ DocumentId = {DocumentId}, Signature = <redacted> }}";
}
