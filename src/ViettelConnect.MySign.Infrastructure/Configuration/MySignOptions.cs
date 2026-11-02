using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Infrastructure.Configuration;

public sealed class MySignOptions
{
    public const string SectionName = "Viettel:MySign";

    public string BaseUrl { get; set; } = "https://remotesigning.viettel.vn/vtss/service";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="CredentialsMode.Static"/> (mặc định): bốn giá trị trên lấy từ options và bắt buộc.
    /// <see cref="CredentialsMode.Dynamic"/>: lấy theo lời gọi qua <c>IMySignCredentialsAccessor</c>.
    /// </summary>
    public CredentialsMode CredentialsMode { get; set; } = CredentialsMode.Static;

    public SignMode SignMode { get; set; } = SignMode.Polling;
    public PollingOptions Polling { get; set; } = new();
    public TransportRetryOptions TransportRetry { get; set; } = new();
    public ErrorOptions Errors { get; set; } = new();
    public CallbackOptions Callback { get; set; } = new();
    public AdvancedOptions Advanced { get; set; } = new();
}

public sealed class PollingOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed class TransportRetryOptions
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(2);
}

public sealed class ErrorOptions
{
    /// <summary>Chỉ bật khi chẩn đoán: cho phép thông điệp lỗi thô của Viettel xuất hiện trong lỗi.</summary>
    public bool IncludeRawErrorMessage { get; set; }
}

public sealed class CallbackOptions
{
    /// <summary>Cặp client mà Viettel dùng khi gọi API callback của đối tác.</summary>
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Thời gian tối đa kho giao dịch chờ mặc định (trong bộ nhớ) giữ một giao dịch callback chưa có kết quả; quá hạn thì
    /// bị dọn và callback đến muộn bị từ chối như giao dịch lạ. Mặc định 24 giờ.
    /// </summary>
    public TimeSpan PendingMaxAge { get; set; } = TimeSpan.FromHours(24);
}

public sealed class AdvancedOptions
{
    /// <summary>
    /// Giá trị <c>async</c> dùng cho chế độ hỏi trạng thái. Tài liệu Viettel ghi 1 ở một mục và 2 ở mục khác;
    /// mặc định 2, chưa kiểm chứng với MySign thật.
    /// </summary>
    public int PollingAsyncCode { get; set; } = 2;

    /// <summary>
    /// Thời gian chờ tối đa của một lời gọi HTTP tới MySign (gồm cả các lần thử lại bên trong). Ở chế độ đồng bộ,
    /// signHash chờ người ký bấm xác nhận trên điện thoại nên phải đủ dài. Để trống (mặc định) =
    /// <c>Polling.TotalTimeout</c> + 1 phút (6 phút với mặc định). Không bao giờ vô hạn.
    /// </summary>
    public TimeSpan? HttpTimeout { get; set; }
}
