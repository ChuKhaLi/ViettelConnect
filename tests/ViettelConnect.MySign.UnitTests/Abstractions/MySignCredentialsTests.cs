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
    public void CacheKey_is_a_stable_sha256_hex_hash()
    {
        Assert.Matches("^[0-9a-f]{64}$", Creds.CacheKey);
        Assert.Equal(Creds.CacheKey, new MySignCredentials("client-abc", "other-secret", "profile-xyz", "user-123").CacheKey);
    }

    [Fact]
    public void CacheKey_contains_no_signer_or_client_identifier()
    {
        var key = Creds.CacheKey;
        Assert.DoesNotContain("profile-xyz", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user-123", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client-abc", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("s3cr3t-value", key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CacheKey_differs_per_tenant()
        => Assert.NotEqual(Creds.CacheKey, (Creds with { ClientId = "client-other" }).CacheKey);

    [Fact]
    public void CacheKey_differs_per_signer()
    {
        Assert.NotEqual(Creds.CacheKey, (Creds with { ProfileId = "profile-other" }).CacheKey);
        Assert.NotEqual(Creds.CacheKey, (Creds with { UserId = "user-other" }).CacheKey);
    }

    [Fact]
    public void CacheKey_is_computed_once_and_the_same_instance_is_returned()
    {
        var creds = new MySignCredentials("c", "s", "p", "u");
        Assert.Same(creds.CacheKey, creds.CacheKey);
    }

    [Fact]
    public void With_expression_after_cache_key_was_computed_yields_the_new_key()
    {
        var original = new MySignCredentials("c", "s", "p", "u");
        var originalKey = original.CacheKey;

        var changed = original with { UserId = "u2" };

        Assert.NotEqual(originalKey, changed.CacheKey);
        Assert.Equal(new MySignCredentials("c", "s", "p", "u2").CacheKey, changed.CacheKey);
        Assert.Equal(originalKey, original.CacheKey);
    }

    [Fact]
    public void Equality_is_by_the_four_credentials_regardless_of_cache_state()
    {
        var computed = new MySignCredentials("c", "s", "p", "u");
        _ = computed.CacheKey;
        var fresh = new MySignCredentials("c", "s", "p", "u");

        Assert.Equal(fresh, computed);
        Assert.Equal(fresh.GetHashCode(), computed.GetHashCode());
        Assert.NotEqual(fresh, computed with { ClientSecret = "other" });
        Assert.NotEqual(fresh, computed with { UserId = "x" });
    }

    [Fact]
    public void CacheKey_is_not_ambiguous_when_ids_contain_the_separator()
        => Assert.NotEqual(
            new MySignCredentials("a|b", "s", "c", "d").CacheKey,
            new MySignCredentials("a", "s", "b|c", "d").CacheKey);
}
