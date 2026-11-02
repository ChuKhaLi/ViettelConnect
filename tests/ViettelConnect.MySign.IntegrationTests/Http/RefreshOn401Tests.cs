using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Infrastructure.Http;
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
    public async Task SignHash_401_logs_in_again_once_and_the_retry_succeeds()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.RequireBearer = true;
        var pipeline = TestPipeline.Create(fake.BaseUrl);
        var request = new SignHashWireRequest("cred-001", [("doc-1", "RG9jMQ==", new byte[] { 1, 2, 3 })], "RGVzYw==", 0, null);

        await pipeline.Wire.SignHashAsync(Creds, request, CancellationToken.None);
        Assert.Equal(1, fake.LoginCount);

        fake.RevokeIssuedTokens();
        var response = await pipeline.Wire.SignHashAsync(Creds, request, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(2, fake.LoginCount);
        // 1 lần đầu + 1 lần bị 401 + 1 lần gửi lại sau khi đăng nhập lại.
        Assert.Equal(3, fake.RequestsFor("signHash").Count);
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

    /// <summary>Chuyển tiếp tới fake thật, trừ signHash: ném lỗi mạng/timeout và đếm số lần gửi.</summary>
    private sealed class SignHashFailingHandler(Func<Exception> failure) : DelegatingHandler(new HttpClientHandler())
    {
        private int _signHashCalls;

        public int SignHashCalls => Volatile.Read(ref _signHashCalls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/signHash", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _signHashCalls);
                throw failure();
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    public static TheoryData<string> SignHashTransportFailures => new() { "http-request", "timeout" };

    [Theory]
    [MemberData(nameof(SignHashTransportFailures))]
    public async Task SignHash_transport_failure_is_a_single_attempt_transport_error(string kind)
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var handler = new SignHashFailingHandler(() => kind == "timeout"
            ? new TaskCanceledException("timeout", new TimeoutException())
            : new HttpRequestException("connection reset"));
        var pipeline = TestPipeline.Create(fake.BaseUrl, handler, maxAttempts: 3);
        var request = new SignHashWireRequest("cred-001", [("doc-1", "RG9jMQ==", new byte[] { 1, 2, 3 })], "RGVzYw==", 0, null);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => pipeline.Wire.SignHashAsync(Creds, request, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Transport, ex.Category);
        Assert.Equal(1, handler.SignHashCalls);
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

    private sealed class CapturingLogger : ILogger<MySignAuthHandler>
    {
        public ConcurrentQueue<(LogLevel Level, string Text)> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task A_401_caller_that_joins_a_flight_returning_the_stale_token_refreshes_once_more()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.RequireBearer = true;
        var pipeline = TestPipeline.Create(fake.BaseUrl);

        await pipeline.Wire.GetCertificatesAsync(Creds, CancellationToken.None);
        var stale = await pipeline.Cache.TryGetAsync(Creds.CacheKey, CancellationToken.None);
        Assert.NotNull(stale);
        fake.RevokeIssuedTokens();

        // Một lượt single-flight khác (vd người gọi thấy cache cũ rồi đọc lại đúng token cũ) đang chạy và sẽ trả token cũ.
        var gate = new TaskCompletionSource<AccessToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var other = pipeline.SingleFlight.RefreshAsync(Creds.CacheKey, _ => gate.Task, CancellationToken.None);

        var call = pipeline.Wire.GetCertificatesAsync(Creds, CancellationToken.None);
        // Chờ có giới hạn: nếu request thứ hai không bao giờ tới thì thất bại thay vì treo.
        var waitWatch = System.Diagnostics.Stopwatch.StartNew();
        while (fake.RequestsFor("certificates/info").Count < 2)
        {
            Assert.True(waitWatch.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting for the 401 request to arrive.");
            await Task.Delay(10);
        }

        // Cho người gọi 401 kịp nhập vào lượt đang chạy rồi mới mở cổng.
        await Task.Delay(200);
        gate.SetResult(stale!);
        await other;

        var certs = await call;

        Assert.NotEmpty(certs);
        Assert.Equal(2, fake.LoginCount);
        Assert.Equal(3, fake.RequestsFor("certificates/info").Count);
    }

    [Fact]
    public async Task Token_with_lifetime_shorter_than_the_expiry_margin_is_still_reused()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ExpiresIn = "30";
        var logger = new CapturingLogger();
        var pipeline = TestPipeline.Create(fake.BaseUrl, authLogger: logger);
        var creds = Creds with { UserId = "user-short-lifetime-" + Guid.NewGuid().ToString("N") };

        await pipeline.Wire.GetCertificatesAsync(creds, CancellationToken.None);
        pipeline.Time.Advance(TimeSpan.FromSeconds(10));
        await pipeline.Wire.GetCertificatesAsync(creds, CancellationToken.None);

        Assert.Equal(1, fake.LoginCount);

        // Biên rút gọn = nửa thời hạn (15s): quá đó thì đăng nhập lại, và cảnh báo chỉ ghi một lần cho mỗi khoá.
        pipeline.Time.Advance(TimeSpan.FromSeconds(6));
        await pipeline.Wire.GetCertificatesAsync(creds, CancellationToken.None);
        Assert.Equal(2, fake.LoginCount);

        var warnings = logger.Lines.Where(l => l.Level == LogLevel.Warning).ToArray();
        var warning = Assert.Single(warnings);
        Assert.DoesNotContain(fake.TokenIssuedFor(creds.ClientId), warning.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-token", warning.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(creds.UserId, warning.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Short_lifetime_warning_is_scoped_to_the_container_not_the_process()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ExpiresIn = "30";
        var creds = Creds with { UserId = "user-warn-scope-" + Guid.NewGuid().ToString("N") };

        // Hai "container" độc lập (mỗi pipeline có SingleFlightRefresh riêng) dùng cùng một khoá: mỗi bên cảnh báo đúng một lần.
        foreach (var _ in new[] { 1, 2 })
        {
            var logger = new CapturingLogger();
            var pipeline = TestPipeline.Create(fake.BaseUrl, authLogger: logger);
            await pipeline.Wire.GetCertificatesAsync(creds, CancellationToken.None);
            await pipeline.Wire.GetCertificatesAsync(creds, CancellationToken.None);

            Assert.Single(logger.Lines.Where(l => l.Level == LogLevel.Warning));
        }
    }
}
