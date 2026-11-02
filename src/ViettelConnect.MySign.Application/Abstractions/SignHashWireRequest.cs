namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>Yêu cầu signHash ở mức dây: tên/mô tả đã mã hoá, băm SHA256 thô. Không lộ ra API công khai.</summary>
internal sealed record SignHashWireRequest(
    string CredentialId,
    IReadOnlyList<(string DocumentId, string EncodedName, byte[] Sha256)> Documents,
    string? EncodedDescription,
    int AsyncCode,
    string? Sad);
