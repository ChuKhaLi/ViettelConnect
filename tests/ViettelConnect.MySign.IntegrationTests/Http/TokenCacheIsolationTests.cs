using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.IntegrationTests.Fake;
using Xunit;

namespace ViettelConnect.MySign.IntegrationTests.Http;

public class TokenCacheIsolationTests
{
    private static readonly MySignCredentials LabA = new("client-A", "secret-A", "profile-1", "user-1");
    private static readonly MySignCredentials LabB = new("client-B", "secret-B", "profile-1", "user-1");

    [Fact]
    public async Task Two_tenants_in_parallel_log_in_once_each_and_never_share_tokens()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.RequireBearer = true;
        var pipeline = TestPipeline.Create(fake.BaseUrl);

        var calls = new List<Task>();
        for (var i = 0; i < 20; i++)
        {
            calls.Add(Task.Run(() => pipeline.Wire.GetCertificatesAsync(LabA, CancellationToken.None)));
            calls.Add(Task.Run(() => pipeline.Wire.GetCertificatesAsync(LabB, CancellationToken.None)));
        }

        await Task.WhenAll(calls);

        Assert.Equal(2, fake.LoginCount);

        var tokenA = "Bearer " + fake.TokenIssuedFor("client-A");
        var tokenB = "Bearer " + fake.TokenIssuedFor("client-B");
        Assert.NotEqual(tokenA, tokenB);

        var certRequests = fake.RequestsFor("certificates/info");
        Assert.Equal(40, certRequests.Count);
        foreach (var request in certRequests)
        {
            using var doc = request.Json();
            var clientId = doc.RootElement.GetProperty("client_id").GetString();
            Assert.Equal(clientId == "client-A" ? tokenA : tokenB, request.Authorization);
        }
    }

    [Fact]
    public async Task Slow_login_of_one_tenant_does_not_block_another_tenant()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.RequireBearer = true;
        var gate = new TaskCompletionSource();
        fake.Scenario.LoginGateByClient["client-A"] = gate.Task;
        var pipeline = TestPipeline.Create(fake.BaseUrl);

        var slow = pipeline.Wire.GetCertificatesAsync(LabA, CancellationToken.None);
        var fast = await pipeline.Wire.GetCertificatesAsync(LabB, CancellationToken.None);

        Assert.NotEmpty(fast);
        Assert.False(slow.IsCompleted);

        gate.SetResult();
        Assert.NotEmpty(await slow);
    }

    [Fact]
    public async Task Token_is_reused_until_it_is_within_the_safety_margin_of_expiry()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.RequireBearer = true;
        fake.Scenario.ExpiresIn = "3600";
        var pipeline = TestPipeline.Create(fake.BaseUrl);

        await pipeline.Wire.GetCertificatesAsync(LabA, CancellationToken.None);
        pipeline.Time.Advance(TimeSpan.FromSeconds(3600 - 120));
        await pipeline.Wire.GetCertificatesAsync(LabA, CancellationToken.None);
        Assert.Equal(1, fake.LoginCount);

        // Còn 30s < biên an toàn 60s -> coi như đã hết hạn, đăng nhập lại.
        pipeline.Time.Advance(TimeSpan.FromSeconds(90));
        await pipeline.Wire.GetCertificatesAsync(LabA, CancellationToken.None);
        Assert.Equal(2, fake.LoginCount);
    }

    [Fact]
    public async Task Login_request_itself_carries_no_bearer()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.RequireBearer = true;
        var pipeline = TestPipeline.Create(fake.BaseUrl);

        await pipeline.Wire.GetCertificatesAsync(LabA, CancellationToken.None);

        Assert.All(fake.RequestsFor("ras/v1/login"), r => Assert.Null(r.Authorization));
    }
}
