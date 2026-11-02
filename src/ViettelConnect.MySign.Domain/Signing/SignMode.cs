namespace ViettelConnect.MySign.Domain.Signing;

/// <summary>Cách chờ kết quả ký: đồng bộ (một lời gọi) hoặc hỏi trạng thái định kỳ.</summary>
public enum SignMode
{
    Synchronous,
    Polling,
}
