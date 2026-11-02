namespace ViettelConnect.MySign.Domain.Errors;

public class MySignException : Exception
{
    public MySignException(
        MySignErrorCategory category,
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null)
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
    }

    public MySignErrorCategory Category { get; }
    public string? RawCode { get; }
    public string Detail { get; }
    public string CorrelationId { get; }
}
