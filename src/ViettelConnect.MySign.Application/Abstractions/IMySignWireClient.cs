using ViettelConnect.MySign.Domain.Certificates;

namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>Cổng mức dây tới MySign; mỗi lời gọi mang bộ thông tin đăng nhập của đúng người ký/đơn vị.</summary>
internal interface IMySignWireClient
{
    Task<AccessToken> LoginAsync(MySignCredentials c, CancellationToken ct);

    Task<IReadOnlyList<Certificate>> GetCertificatesAsync(MySignCredentials c, CancellationToken ct);

    Task<SignHashWireResponse> SignHashAsync(MySignCredentials c, SignHashWireRequest r, CancellationToken ct);

    Task<StatusSnapshot> GetStatusAsync(MySignCredentials c, string transactionId, CancellationToken ct);
}
