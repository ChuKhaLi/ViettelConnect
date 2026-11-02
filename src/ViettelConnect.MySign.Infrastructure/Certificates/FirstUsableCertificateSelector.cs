using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Certificates;
using ViettelConnect.MySign.Domain.Errors;

namespace ViettelConnect.MySign.Infrastructure.Certificates;

/// <summary>Chọn chứng thư đầu tiên có khoá bật và chưa hết hạn (<c>ValidTo</c> null hoặc sau hiện tại).</summary>
internal sealed class FirstUsableCertificateSelector : ICertificateSelector
{
    private readonly TimeProvider _time;
    private readonly ICorrelationIdAccessor _correlation;

    public FirstUsableCertificateSelector(TimeProvider time, ICorrelationIdAccessor correlation)
    {
        _time = time;
        _correlation = correlation;
    }

    public Task<Certificate> SelectAsync(IReadOnlyList<Certificate> certificates, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        var now = _time.GetUtcNow();
        foreach (var cert in certificates)
        {
            if (cert.KeyEnabled && (cert.ValidTo is null || cert.ValidTo > now))
            {
                return Task.FromResult(cert);
            }
        }

        throw new MySignException(
            MySignErrorCategory.NoActiveCertificate,
            rawCode: null,
            detail: "Người ký không có chứng thư số nào còn hiệu lực và đang bật khoá.",
            correlationId: _correlation.Current);
    }
}
