using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ViettelConnect.MySign.Client;
using ViettelConnect.MySign.Client.DependencyInjection;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Infrastructure.Configuration;
using ViettelConnect.MySign.IntegrationTests.Fake;
using Xunit;

namespace ViettelConnect.MySign.IntegrationTests.EndToEnd;

/// <summary>Hành vi của pipeline HttpClient khi dựng thật bằng <c>AddViettelMySign</c> (retry, timeout, huỷ).</summary>
public class HttpPipelineThroughDiTests
{
    private static ServiceProvider Build(FakeMySignServer fake, Action<MySignOptions>? extra = null, int registrations = 1)
    {
        var services = new ServiceCollection();
        for (var i = 0; i < registrations; i++)
        {
            services.AddViettelMySign(o =>
            {
                o.BaseUrl = fake.BaseUrl;
                o.ClientId = "client-A";
                o.ClientSecret = "secret-A";
                o.ProfileId = "profile-1";
                o.UserId = "user-1";
                o.TransportRetry.MaxAttempts = 3;
                o.TransportRetry.BaseDelay = TimeSpan.FromMilliseconds(1);
                o.TransportRetry.MaxDelay = TimeSpan.FromMilliseconds(2);
                extra?.Invoke(o);
            });
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Each_request_goes_through_exactly_one_retry_handler_even_if_registered_twice(int registrations)
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["certificates/info"] = (503, string.Empty);
        await using var sp = Build(fake, registrations: registrations);
        await using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IViettelMySignClient>();

        var ex = await Assert.ThrowsAsync<MySignException>(() => client.ListCertificatesAsync());

        Assert.Equal(MySignErrorCategory.Transport, ex.Category);
        Assert.Equal(3, fake.RequestsFor("certificates/info").Count);
    }

    [Fact]
    public async Task HttpClient_timeout_surfaces_as_a_transport_error_not_a_cancellation()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.DelayByRoute["certificates/info"] = TimeSpan.FromSeconds(30);
        await using var sp = Build(fake, o =>
        {
            o.Advanced.HttpTimeout = TimeSpan.FromMilliseconds(300);
            o.TransportRetry.MaxAttempts = 1;
        });
        await using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IViettelMySignClient>();

        var watch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<MySignException>(() => client.ListCertificatesAsync());

        Assert.Equal(MySignErrorCategory.Transport, ex.Category);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "The call must end at the HttpClient timeout, not wait for the server.");
    }

    [Fact]
    public async Task Caller_cancellation_surfaces_as_operation_canceled()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.DelayByRoute["certificates/info"] = TimeSpan.FromSeconds(30);
        await using var sp = Build(fake, o => o.TransportRetry.MaxAttempts = 1);
        await using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IViettelMySignClient>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListCertificatesAsync(cts.Token));
    }
}
