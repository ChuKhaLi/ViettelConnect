namespace ViettelConnect.MySign.Domain.Errors;

/// <summary>
/// Hết thời gian chờ người ký xác nhận (hoặc hết thời gian hỏi trạng thái). Ném ở mọi chế độ ký: đồng bộ (mã
/// Viettel 58061), hỏi trạng thái (trạng thái 4001 hoặc hết <c>Polling:TotalTimeout</c>).
/// </summary>
public sealed class MySignTimeoutException : MySignException
{
    public MySignTimeoutException(
        string detail,
        string correlationId,
        string? transactionId = null,
        string? rawCode = null,
        Exception? inner = null)
        : base(MySignErrorCategory.SignTimeout, rawCode, detail, correlationId, inner, transactionId)
    {
    }
}
