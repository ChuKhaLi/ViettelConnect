using ViettelConnect.MySign.Domain.Signing;
using ViettelConnect.MySign.Infrastructure.Configuration;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Configuration;

public sealed class MySignOptionsValidatorTests
{
    private static MySignOptions Valid() => new()
    {
        ClientId = "c",
        ClientSecret = "s",
        ProfileId = "p",
        UserId = "u",
    };

    private static Microsoft.Extensions.Options.ValidateOptionsResult Run(MySignOptions o)
        => new MySignOptionsValidator().Validate(null, o);

    [Fact]
    public void Defaults_with_static_credentials_are_valid()
    {
        var o = Valid();
        Assert.True(Run(o).Succeeded);
        Assert.Equal("https://remotesigning.viettel.vn/vtss/service", o.BaseUrl);
        Assert.Equal(SignMode.Polling, o.SignMode);
        Assert.Equal(2, o.Advanced.PollingAsyncCode);
        Assert.False(o.Errors.IncludeRawErrorMessage);
        Assert.Equal(TimeSpan.FromSeconds(3), o.Polling.Interval);
        Assert.Equal(TimeSpan.FromMinutes(5), o.Polling.TotalTimeout);
        Assert.Equal(TimeSpan.FromHours(1), o.Callback.TokenLifetime);
        Assert.Equal(TimeSpan.FromHours(24), o.Callback.PendingMaxAge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Callback_pending_max_age_must_be_positive(int hours)
    {
        var o = Valid();
        o.Callback.PendingMaxAge = TimeSpan.FromHours(hours);
        var r = Run(o);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, f => f.Contains("Callback:PendingMaxAge", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ClientId")]
    [InlineData("ClientSecret")]
    [InlineData("ProfileId")]
    [InlineData("UserId")]
    public void Static_mode_requires_each_credential(string field)
    {
        var o = Valid();
        typeof(MySignOptions).GetProperty(field)!.SetValue(o, "");
        var r = Run(o);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, f => f.Contains(field));
    }

    [Theory]
    [InlineData("cb", "")]
    [InlineData("", "sec")]
    [InlineData("cb", "  ")]
    public void Callback_client_id_and_secret_must_be_set_together(string id, string secret)
    {
        var o = Valid();
        o.Callback.ClientId = id;
        o.Callback.ClientSecret = secret;
        var r = Run(o);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, f => f.Contains("Callback"));
    }

    [Fact]
    public void Callback_disabled_or_fully_configured_is_valid()
    {
        var o = Valid();
        Assert.True(Run(o).Succeeded);
        o.Callback.ClientId = "cb";
        o.Callback.ClientSecret = "sec";
        Assert.True(Run(o).Succeeded);
    }

    [Fact]
    public void Dynamic_mode_does_not_require_credentials()
        => Assert.True(Run(new MySignOptions { CredentialsMode = CredentialsMode.Dynamic }).Succeeded);

    [Theory]
    [InlineData("http://remotesigning.viettel.vn/vtss/service")]
    [InlineData("not a url")]
    [InlineData("")]
    public void BaseUrl_must_be_https(string url)
    {
        var o = Valid();
        o.BaseUrl = url;
        Assert.False(Run(o).Succeeded);
    }

    [Fact]
    public void BaseUrl_allows_loopback_http_for_fake_server()
    {
        var o = Valid();
        o.BaseUrl = "http://127.0.0.1:5000/vtss/service";
        Assert.True(Run(o).Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void PollingAsyncCode_must_be_1_or_2(int code)
    {
        var o = Valid();
        o.Advanced.PollingAsyncCode = code;
        Assert.False(Run(o).Succeeded);
    }

    [Fact]
    public void PollingAsyncCode_1_is_accepted()
    {
        var o = Valid();
        o.Advanced.PollingAsyncCode = 1;
        Assert.True(Run(o).Succeeded);
    }

    [Fact]
    public void Polling_and_retry_bounds_are_checked()
    {
        var o = Valid();
        o.Polling.Interval = TimeSpan.Zero;
        o.Polling.TotalTimeout = TimeSpan.Zero;
        o.TransportRetry.MaxAttempts = 0;
        o.TransportRetry.MaxDelay = TimeSpan.Zero;
        o.Callback.TokenLifetime = TimeSpan.Zero;
        var r = Run(o);
        Assert.False(r.Succeeded);
        Assert.True(r.Failures!.Count() >= 5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Polling_interval_must_be_positive(int seconds)
    {
        var o = Valid();
        o.Polling.Interval = TimeSpan.FromSeconds(seconds);
        var r = Run(o);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, f => f.Contains("Polling:Interval must be > 0"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Polling_total_timeout_must_be_positive(int seconds)
    {
        var o = Valid();
        o.Polling.TotalTimeout = TimeSpan.FromSeconds(seconds);
        var r = Run(o);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, f => f.Contains("Polling:TotalTimeout"));
    }

    [Fact]
    public void Http_timeout_is_optional_but_must_be_positive_and_finite_when_set()
    {
        var o = Valid();
        Assert.Null(o.Advanced.HttpTimeout);
        Assert.True(Run(o).Succeeded);

        o.Advanced.HttpTimeout = TimeSpan.FromMinutes(2);
        Assert.True(Run(o).Succeeded);

        foreach (var bad in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), Timeout.InfiniteTimeSpan })
        {
            o.Advanced.HttpTimeout = bad;
            var r = Run(o);
            Assert.False(r.Succeeded);
            Assert.Contains(r.Failures!, f => f.Contains("Advanced:HttpTimeout"));
        }
    }

    [Fact]
    public void Undefined_SignMode_or_CredentialsMode_fails()
    {
        var o = Valid();
        o.SignMode = (SignMode)99;
        o.CredentialsMode = (CredentialsMode)99;
        Assert.Equal(2, Run(o).Failures!.Count());
    }
}
