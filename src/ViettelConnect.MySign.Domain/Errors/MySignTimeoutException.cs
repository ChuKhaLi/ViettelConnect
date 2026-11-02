namespace ViettelConnect.MySign.Domain.Errors;

/// <summary>Hết thời gian chờ người ký xác nhận (hoặc hết thời gian hỏi trạng thái).</summary>
public sealed class MySignTimeoutException : MySignException
{
    public MySignTimeoutException(
        string detail,
        string correlationId,
        string? transactionId = null,
        string? rawCode = null,
        Exception? inner = null)
        : base(MySignErrorCategory.SignTimeout, rawCode, detail, correlationId, inner)
    {
        TransactionId = transactionId;
    }

    public string? TransactionId { get; }
}
