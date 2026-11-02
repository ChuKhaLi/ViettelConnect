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
        Category = category;
        RawCode = rawCode;
        Detail = detail;

        // Accessor của ứng dụng trả mã rỗng không được che lỗi thật bằng ArgumentException: sinh mã mới.
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId;
        TransactionId = transactionId;
    }

    public MySignErrorCategory Category { get; }
    public string? RawCode { get; }
    public string Detail { get; }
    /// <summary>Mã tương quan của lời gọi; nếu nơi ném lỗi không có mã hợp lệ (rỗng/khoảng trắng) thì là GUID mới dạng "N".</summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Mã giao dịch MySign khi lỗi xảy ra sau khi yêu cầu ký đã được Viettel nhận (người ký có thể đã thấy lời nhắc);
    /// null nếu lỗi xảy ra trước đó. Dùng để đối soát, không phải bí mật.
    /// </summary>
    public string? TransactionId { get; }
}
