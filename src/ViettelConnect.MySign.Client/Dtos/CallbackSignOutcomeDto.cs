namespace ViettelConnect.MySign.Client.Dtos;

/// <summary>Trạng thái của giao dịch ký do callback báo về.</summary>
public enum CallbackSignStatus
{
    /// <summary>Chưa có kết cục cuối; giao dịch vẫn được giữ chờ.</summary>
    Pending,

    /// <summary>Người ký đã xác nhận; có chữ ký.</summary>
    Succeeded,

    /// <summary>Người ký từ chối.</summary>
    Rejected,

    /// <summary>Hết hạn chờ người ký xác nhận.</summary>
    TimedOut,

    /// <summary>Ký thất bại vì lý do khác (xem <see cref="CallbackSignOutcomeDto.RawStatus"/>).</summary>
    Failed,
}

/// <summary>
/// Kết cục ký do Viettel báo về qua callback. Chỉ phát đúng một lần cho mỗi giao dịch (bản gửi trùng không có kết cục).
/// </summary>
/// <param name="TenantKey">Đơn vị (lab) sở hữu giao dịch, đúng giá trị đã truyền cho <c>BeginSignHashesAsync</c>.</param>
/// <param name="TransactionId">Mã giao dịch trả về từ <c>BeginSignHashesAsync</c>.</param>
/// <param name="Status">Trạng thái đã phân loại.</param>
/// <param name="Signatures">Chữ ký theo thứ tự tài liệu đã gửi; rỗng trừ khi <see cref="Status"/> là Succeeded.</param>
/// <param name="RawStatus">Mã trạng thái gốc của Viettel (để chẩn đoán).</param>
public sealed record CallbackSignOutcomeDto(
    string TenantKey,
    string TransactionId,
    CallbackSignStatus Status,
    IReadOnlyList<DocumentSignatureDto> Signatures,
    string? RawStatus)
{
    public override string ToString() =>
        $"CallbackSignOutcomeDto {{ TenantKey = {TenantKey}, TransactionId = {TransactionId}, Status = {Status}, Signatures = {Signatures.Count} }}";
}
