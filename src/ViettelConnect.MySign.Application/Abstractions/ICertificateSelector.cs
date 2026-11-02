using ViettelConnect.MySign.Domain.Certificates;

namespace ViettelConnect.MySign.Application.Abstractions;

public interface ICertificateSelector
{
    Task<Certificate> SelectAsync(IReadOnlyList<Certificate> certificates, CancellationToken ct);
}
