namespace ViettelConnect.MySign.Client.Dtos;

/// <summary>Kết quả bắt đầu ký có callback: mã giao dịch để đối chiếu với kết cục Viettel báo về sau và chứng thư đã chọn.</summary>
public sealed record BeginSignResultDto(string TransactionId, CertificateDto Certificate);
