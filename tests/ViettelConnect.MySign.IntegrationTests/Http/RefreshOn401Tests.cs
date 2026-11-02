using System.Net;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.IntegrationTests.Fake;
using Xunit;

namespace ViettelConnect.MySign.IntegrationTests.Http;

public class RefreshOn401Tests
{
    private static readonly MySignCredentials Creds = new("client-A", "secret-A", "profile-1", "user-1");

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> script) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(script(request));
        }
    }

    /// <summary>Lỗi 502 hai lần đầu rồi chuyển tiếp tới fake thật; ghi lại thân từng lần gửi.</summary>
    private sealed class FlakyHandler(int failFirst) : DelegatingHandler(new HttpClientHandler())
    {
        private int _calls;

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            lock (Bodies)
            {
                Bodies.Add(body);
            }

            if (Interlocked.Increment(ref _calls) <= failFirst)
            {
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task First_401_clears_cache_logs_in_again_once_and_retry_succeeds()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.RequireBearer = true;
        var pipeline = TestPipeline.Create(fake.BaseUrl);

        await pipeline.Wire.GetCertificatesAsync(Creds, CancellationToken.None);
        Assert.Equal(1, fake.LoginCount);

        fake.RevokeIssuedTokens();
        var certs = await pipeline.Wire.GetCertificatesAsync(Creds, CancellationToken.None);

        Assert.NotEmpty(certs);
        Assert.Equal(2, fake.LoginCount);
        Assert.Equal(3, fake.RequestsFor("certificates/info").Count);
    }

    [Fact]
    public async Task Retried_request_resends_the_same_body()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.RequireBearer = true;
        var pipeline = TestPipeline.Create(fake.BaseUrl);

        await pipeline.Wire.GetCertificatesAsync(Creds, CancellationToken.None);
        fake.RevokeIssuedTokens();
        await pipeline.Wire.GetCertificatesAsync(Creds, CancellationToken.None);

        var bodies = fake.RequestsFor("certificates/info").Select(r => r.Body).ToArray();
        Assert.Equal(3, bodies.Length);
        Assert.All(bodies, b => Assert.False(string.IsNullOrWhiteSpace(b)));
        Assert.Single(bodies.Distinct());
    }

    [Fact]
    public async Task Second_consecutive_401_throws_authentication_after_exactly_one_relogin()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.RequireBearer = true;
        fake.Scenario.ErrorByRoute["certificates/info"] =
            (401, "{\"error\":\"59033\",\"error_description\":\"invalid token\"}");
        var pipeline = TestPipeline.Create(fake.BaseUrl);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => pipeline.Wire.GetCertificatesAsync(Creds, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Authentication, ex.Category);
        Assert.Equal(2, fake.LoginCount);
        Assert.Equal(2, fake.RequestsFor("certificates/info").Count);
        Assert.Null(await pipeline.Cache.TryGetAsync(Creds.CacheKey, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_401s_share_a_single_relogin()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.RequireBearer = true;
        var pipeline = TestPipeline.Create(fake.BaseUrl);

        await pipeline.Wire.GetCertificatesAsync(Creds, CancellationToken.None);
        fake.RevokeIssuedTokens();

        await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => pipeline.Wire.GetCertificatesAsync(Creds, CancellationToken.None))));

        Assert.Equal(2, fake.LoginCount);
    }

    [Fact]
    public async Task Transient_5xx_is_retried_and_the_body_is_resent()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var flaky = new FlakyHandler(failFirst: 2);
        var pipeline = TestPipeline.Create(fake.BaseUrl, flaky);

        var token = await pipeline.Wire.LoginAsync(Creds, CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(token.Value));
        Assert.Equal(3, flaky.Bodies.Count);
        Assert.All(flaky.Bodies, b => Assert.False(string.IsNullOrWhiteSpace(b)));
        Assert.Single(flaky.Bodies.Distinct());
        Assert.Equal(1, fake.LoginCount);
    }

    [Fact]
    public async Task Exhausted_transient_retries_throw_transport_exception()
    {
        var terminal = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var pipeline = TestPipeline.Create("http://127.0.0.1:1", terminal, maxAttempts: 3);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => pipeline.Wire.LoginAsync(Creds, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Transport, ex.Category);
        Assert.Equal(3, terminal.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_retried()
    {
        var terminal = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var pipeline = TestPipeline.Create("http://127.0.0.1:1", terminal);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.Wire.LoginAsync(Creds, cts.Token));

        Assert.Equal(0, terminal.Calls);
    }
}
