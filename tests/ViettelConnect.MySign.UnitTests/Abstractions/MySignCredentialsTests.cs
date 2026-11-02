using ViettelConnect.MySign.Application.Abstractions;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Abstractions;

public sealed class MySignCredentialsTests
{
    private static readonly MySignCredentials Creds = new("client-abc", "s3cr3t-value", "profile-xyz", "user-123");

    [Fact]
    public void ToString_hides_secret_profile_and_user()
    {
        var text = Creds.ToString();
        Assert.DoesNotContain("s3cr3t-value", text);
        Assert.DoesNotContain("profile-xyz", text);
        Assert.DoesNotContain("user-123", text);
        Assert.Contains("client-abc", text);
    }

    [Fact]
    public void CacheKey_combines_client_profile_and_user()
        => Assert.Equal("client-abc|profile-xyz|user-123", Creds.CacheKey);

    [Fact]
    public void CacheKey_differs_per_tenant()
        => Assert.NotEqual(Creds.CacheKey, (Creds with { ClientId = "client-other" }).CacheKey);
}
