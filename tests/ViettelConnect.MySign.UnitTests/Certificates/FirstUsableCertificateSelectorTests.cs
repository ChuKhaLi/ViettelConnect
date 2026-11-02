using Microsoft.Extensions.Time.Testing;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Certificates;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Infrastructure.Certificates;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Certificates;

public sealed class FirstUsableCertificateSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    private sealed class FixedCorrelation : ICorrelationIdAccessor
    {
        public string Current => "corr-1";
    }

    private static Certificate Cert(string id, bool enabled = true, DateTimeOffset? validTo = null)
        => new(id, null, null, null, null, validTo, enabled, Array.Empty<string>(), Array.Empty<byte[]>());

    private static FirstUsableCertificateSelector Create()
        => new(new FakeTimeProvider(Now), new FixedCorrelation());

    [Fact]
    public async Task Skips_disabled_and_expired_picks_first_usable()
    {
        var certs = new[]
        {
            Cert("disabled", enabled: false),
            Cert("expired", validTo: Now.AddDays(-1)),
            Cert("good", validTo: Now.AddDays(30)),
            Cert("later"),
        };

        var picked = await Create().SelectAsync(certs, CancellationToken.None);

        Assert.Equal("good", picked.CredentialId);
    }

    [Fact]
    public async Task Null_ValidTo_is_usable()
    {
        var picked = await Create().SelectAsync(new[] { Cert("open") }, CancellationToken.None);
        Assert.Equal("open", picked.CredentialId);
    }

    [Fact]
    public async Task ValidTo_equal_to_now_is_expired()
    {
        var ex = await Assert.ThrowsAsync<MySignException>(
            () => Create().SelectAsync(new[] { Cert("edge", validTo: Now) }, CancellationToken.None));
        Assert.Equal(MySignErrorCategory.NoActiveCertificate, ex.Category);
    }

    [Fact]
    public async Task None_usable_throws_NoActiveCertificate_with_correlation_id()
    {
        var ex = await Assert.ThrowsAsync<MySignException>(
            () => Create().SelectAsync(new[] { Cert("d", enabled: false) }, CancellationToken.None));
        Assert.Equal(MySignErrorCategory.NoActiveCertificate, ex.Category);
        Assert.Equal("corr-1", ex.CorrelationId);
    }

    [Fact]
    public async Task Empty_list_throws_NoActiveCertificate()
    {
        var ex = await Assert.ThrowsAsync<MySignException>(
            () => Create().SelectAsync(Array.Empty<Certificate>(), CancellationToken.None));
        Assert.Equal(MySignErrorCategory.NoActiveCertificate, ex.Category);
    }
}
