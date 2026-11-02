using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>
/// Cài đặt ký mà use case cần, thuần tầng Application (Infrastructure ánh xạ từ <c>MySignOptions</c>).
/// <c>PollingAsyncCode</c> là mã <c>async</c> gửi lên khi ở chế độ hỏi trạng thái (mặc định 2, chưa kiểm chứng).
/// </summary>
internal sealed record MySignSigningSettings(
    SignMode SignMode,
    int PollingAsyncCode,
    TimeSpan PollingInterval,
    TimeSpan PollingTotalTimeout)
{
    public MySignSigningSettings(SignMode signMode)
        : this(signMode, 2, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(5))
    {
    }
}
