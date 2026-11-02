using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Certificates;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>Liệt kê chứng thư số của người ký hiện tại.</summary>
internal sealed class ListCertificates
{
    private readonly IMySignWireClient _wire;
    private readonly IMySignCredentialsAccessor _credentials;

    public ListCertificates(IMySignWireClient wire, IMySignCredentialsAccessor credentials)
    {
        _wire = wire ?? throw new ArgumentNullException(nameof(wire));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    public Task<IReadOnlyList<Certificate>> ExecuteAsync(CancellationToken ct) =>
        _wire.GetCertificatesAsync(_credentials.Get(), ct);
}
