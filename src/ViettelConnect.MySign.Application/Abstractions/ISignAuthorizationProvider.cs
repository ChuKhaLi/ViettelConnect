using ViettelConnect.MySign.Domain.Certificates;

namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>
/// Chỗ chừa cho ký tự động (không cần người ký bấm xác nhận). Bản đầu trả <c>null</c>:
/// người ký xác nhận trên ứng dụng MySign.
/// </summary>
public interface ISignAuthorizationProvider
{
    ValueTask<string?> GetSadAsync(MySignCredentials who, Certificate cert, CancellationToken ct);
}
