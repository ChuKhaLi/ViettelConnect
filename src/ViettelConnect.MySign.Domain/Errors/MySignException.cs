namespace ViettelConnect.MySign.Domain.Errors;

public class MySignException : Exception
{
    public MySignException(
        MySignErrorCategory category,
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null,
        string? transactionId = null)
        : base(detail, inner)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("CorrelationId must be non-empty.", nameof(correlationId));
        }

        Category = category;
        RawCode = rawCode;
        Detail = detail;
        CorrelationId = correlationId;
        TransactionId = transactionId;
    }

    public MySignErrorCategory Category { get; }
    public string? RawCode { get; }
    public string Detail { get; }
    public string CorrelationId { get; }

    /// <summary>
    /// Mã giao dịch MySign khi lỗi xảy ra sau khi yêu cầu ký đã được Viettel nhận (người ký có thể đã thấy lời nhắc);
    /// null nếu lỗi xảy ra trước đó. Dùng để đối soát, không phải bí mật.
    /// </summary>
    public string? TransactionId { get; }
}
