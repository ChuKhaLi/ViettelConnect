namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>Phản hồi signHash: chữ ký (đồng bộ) hoặc mã giao dịch (bất đồng bộ). Ít nhất một trong hai khác null.</summary>
internal sealed record SignHashWireResponse(string? TransactionId, IReadOnlyList<byte[]>? Signatures);
