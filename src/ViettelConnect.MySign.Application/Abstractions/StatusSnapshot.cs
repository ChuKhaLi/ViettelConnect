using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>Kết quả một lần hỏi requests/status. <c>ErrorDescription</c> chỉ có khi bật chẩn đoán.</summary>
internal sealed record StatusSnapshot(
    string? RawStatus,
    SignStatus Status,
    IReadOnlyList<byte[]> Signatures,
    string? ErrorCode,
    string? ErrorDescription);
