namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>
/// Yêu cầu signHash ở mức dây: tên/mô tả đã mã hoá, băm SHA256 thô. Không lộ ra API công khai.
/// Không mang DocumentId của ứng dụng: trên dây mỗi tài liệu được đánh số 1..n theo thứ tự trong <c>Documents</c>.
/// </summary>
internal sealed record SignHashWireRequest(
    string CredentialId,
    IReadOnlyList<(string EncodedName, byte[] Sha256)> Documents,
    string? EncodedDescription,
    int AsyncCode,
    string? Sad);
