using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Certificates;

namespace ViettelConnect.MySign.Infrastructure.Signing;

/// <summary>Mặc định: không có SAD, người ký xác nhận từng yêu cầu trên ứng dụng MySign.</summary>
internal sealed class MobileConfirmationAuthorizationProvider : ISignAuthorizationProvider
{
    public ValueTask<string?> GetSadAsync(MySignCredentials who, Certificate cert, CancellationToken ct)
        => ValueTask.FromResult<string?>(null);
}
