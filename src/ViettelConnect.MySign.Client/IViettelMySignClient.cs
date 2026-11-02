using ViettelConnect.MySign.Client.Dtos;
using ViettelConnect.MySign.Domain.Errors;

namespace ViettelConnect.MySign.Client;

/// <summary>
/// Facade công khai của SDK Viettel MySign: ký mã băm SHA-256, người ký xác nhận từng yêu cầu trên ứng dụng MySign.
/// Đăng ký bằng <c>services.AddViettelMySign(...)</c>; dịch vụ là scoped. Thông tin đăng nhập lấy theo từng lời gọi
/// (chế độ Static: từ options; Dynamic: từ <c>IMySignCredentialsAccessor</c> của ứng dụng).
/// </summary>
/// <remarks>Lỗi nghiệp vụ/giao thức ném <see cref="MySignException"/> (hết thời gian chờ: <see cref="MySignTimeoutException"/>).</remarks>
public interface IViettelMySignClient
{
    /// <summary>Liệt kê chứng thư số của người ký hiện tại.</summary>
    /// <exception cref="MySignException">Đăng nhập thất bại, lỗi Viettel hoặc lỗi kết nối.</exception>
    Task<IReadOnlyList<CertificateDto>> ListCertificatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Ký các mã băm và chờ tới khi có chữ ký: chế độ <c>Polling</c> (mặc định) hỏi trạng thái định kỳ tới hết
    /// <c>Polling:TotalTimeout</c>; chế độ <c>Synchronous</c> chờ trong một lời gọi. Chữ ký trả về đúng thứ tự tài liệu.
    /// </summary>
    /// <exception cref="MySignException">Yêu cầu không hợp lệ, người ký từ chối, ký thất bại, vi phạm giao thức.</exception>
    /// <exception cref="MySignTimeoutException">Người ký không xác nhận kịp.</exception>
    Task<SignHashesResultDto> SignHashesAsync(SignHashesRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Bắt đầu ký có callback: gửi yêu cầu rồi trả ngay mã giao dịch. Viettel báo kết quả sau qua hai endpoint callback
    /// của ứng dụng (<see cref="HandleCallbackTokenRequestAsync"/>, <see cref="HandleCallbackResultAsync"/>).
    /// </summary>
    /// <param name="request">Tài liệu cần ký.</param>
    /// <param name="tenantKey">Đơn vị (lab) sở hữu giao dịch; phải khớp đơn vị của client callback mà Viettel dùng.
    /// Mặc định <c>"default"</c> khớp client callback cấu hình trong <c>Viettel:MySign:Callback</c>.</param>
    /// <param name="ct">Huỷ lời gọi.</param>
    Task<BeginSignResultDto> BeginSignHashesAsync(SignHashesRequestDto request, string tenantKey = "default", CancellationToken ct = default);

    /// <summary>
    /// Xử lý request xác thực Viettel gửi tới endpoint callback của ứng dụng (thân JSON nguyên văn). Không ném lỗi với
    /// đầu vào sai; luôn trả phản hồi để ứng dụng ghi nguyên văn cho Viettel.
    /// </summary>
    Task<CallbackResponseDto> HandleCallbackTokenRequestAsync(string requestJson, CancellationToken ct = default);

    /// <summary>Xử lý request báo kết quả ký Viettel gửi tới endpoint callback của ứng dụng.</summary>
    /// <param name="bearerToken">Token trong header <c>Authorization: Bearer ...</c> (không kèm chữ "Bearer"); null nếu thiếu.</param>
    /// <param name="requestJson">Thân JSON nguyên văn.</param>
    /// <param name="ct">Huỷ lời gọi.</param>
    /// <returns><c>Response</c> để ghi nguyên văn cho Viettel; <c>Outcome</c> khác null khi có kết cục mới cần ứng dụng xử lý
    /// (kết cục cuối chỉ phát đúng một lần cho mỗi giao dịch).</returns>
    Task<(CallbackResponseDto Response, CallbackSignOutcomeDto? Outcome)> HandleCallbackResultAsync(
        string? bearerToken,
        string requestJson,
        CancellationToken ct = default);
}
