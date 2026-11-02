using ViettelConnect.MySign.Domain.Certificates;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>Chữ ký (thô) của một tài liệu.</summary>
public sealed record DocumentSignature(string DocumentId, byte[] Signature);

/// <summary>Kết quả ký: chứng thư đã dùng và chữ ký theo đúng thứ tự tài liệu của yêu cầu.</summary>
public sealed record SignHashesResult(Certificate Certificate, IReadOnlyList<DocumentSignature> Signatures, string? TransactionId);
