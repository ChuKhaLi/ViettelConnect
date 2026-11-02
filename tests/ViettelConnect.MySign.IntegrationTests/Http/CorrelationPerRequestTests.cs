using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Infrastructure.Caching;
using ViettelConnect.MySign.Infrastructure.Configuration;
using ViettelConnect.MySign.Infrastructure.Http;
using ViettelConnect.MySign.Infrastructure.Logging;
using ViettelConnect.MySign.Infrastructure.Wire;
using Xunit;

namespace ViettelConnect.MySign.IntegrationTests.Http;

/// <summary>
/// Handler của IHttpClientFactory sống trong scope riêng (dùng chung giữa các lời gọi đồng thời), nên mã tương quan
/// phải đi theo từng request chứ không lấy từ accessor của handler.
/// </summary>
public class CorrelationPerRequestTests
{
    private const string HandlerScopeId = "handler-scope";

    private sealed class FixedCorrelation(string id) : ICorrelationIdAccessor
    {
        public string Current => id;
    }

    private sealed class NoDelay : IDelayer
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> script) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            script(request);
    }

    private sealed class CapturingLogger : ILogger<MySignCallLogger>
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue(formatter(state, exception));
    }

    private sealed record Pipeline(MySignWireClient CallerA, MySignWireClient CallerB, CapturingLogger HandlerLogger);

    /// <summary>
    /// Dựng pipeline như DI: handler và wire client dùng cho login bên trong handler đều mang accessor của scope handler;
    /// hai người gọi dùng chung HttpClient nhưng có mã tương quan riêng.
    /// </summary>
    private static Pipeline Build(Func<HttpRequestMessage, Task<HttpResponseMessage>> script)
    {
        var options = Options.Create(new MySignOptions
        {
            BaseUrl = "http://127.0.0.1:1/vtss/service",
            TransportRetry = new TransportRetryOptions { MaxAttempts = 2 },
        });
        var handlerScope = new FixedCorrelation(HandlerScopeId);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var logger = new CapturingLogger();

        IMySignWireClient? handlerWire = null;
        var auth = new MySignAuthHandler(() => handlerWire!, new InMemoryTokenCache(), new SingleFlightRefresh(), time, handlerScope)
        {
            InnerHandler = new ScriptedHandler(script),
        };
        var retry = new TransientFailureRetryHandler(options, new NoDelay(), handlerScope, time) { InnerHandler = auth };
        var http = new HttpClient(retry);

        handlerWire = new MySignCallLogger(new MySignWireClient(http, options, handlerScope, time), logger, handlerScope);
        return new Pipeline(
            new MySignWireClient(http, options, new FixedCorrelation("caller-A"), time),
            new MySignWireClient(http, options, new FixedCorrelation("caller-B"), time),
            logger);
    }

    private static bool IsLogin(HttpRequestMessage r) => r.RequestUri!.AbsolutePath.EndsWith("ras/v1/login", StringComparison.Ordinal);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage LoginOk() =>
        Json(HttpStatusCode.OK, "{\"access_token\":\"tok\",\"token_type\":\"Bearer\",\"expires_in\":\"3600\"}");

    private static async Task<(MySignException A, MySignException B)> BothFail(Pipeline p, Func<MySignWireClient, MySignCredentials, Task> call)
    {
        var a = Task.Run(() => Assert.ThrowsAnyAsync<MySignException>(() => call(p.CallerA, new("client-A", "secret-A", "profile", "user-A"))));
        var b = Task.Run(() => Assert.ThrowsAnyAsync<MySignException>(() => call(p.CallerB, new("client-B", "secret-B", "profile", "user-B"))));
        await Task.WhenAll(a, b);
        return (await a, await b);
    }

    [Fact]
    public async Task Retry_exhausted_transport_error_carries_each_callers_own_id()
    {
        var p = Build(r => IsLogin(r)
            ? Task.FromResult(LoginOk())
            : throw new HttpRequestException("connection reset"));

        var (a, b) = await BothFail(p, (w, c) => w.GetCertificatesAsync(c, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Transport, a.Category);
        Assert.Equal(MySignErrorCategory.Transport, b.Category);
        Assert.Equal("caller-A", a.CorrelationId);
        Assert.Equal("caller-B", b.CorrelationId);
    }

    [Fact]
    public async Task Second_401_authentication_error_carries_each_callers_own_id()
    {
        var p = Build(r => Task.FromResult(IsLogin(r)
            ? LoginOk()
            : Json(HttpStatusCode.Unauthorized, "{\"error\":\"59033\"}")));

        var (a, b) = await BothFail(p, (w, c) => w.GetCertificatesAsync(c, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Authentication, a.Category);
        Assert.Equal(MySignErrorCategory.Authentication, b.Category);
        Assert.Equal("caller-A", a.CorrelationId);
        Assert.Equal("caller-B", b.CorrelationId);
    }

    [Fact]
    public async Task Login_triggered_inside_the_auth_handler_throws_and_logs_with_the_originating_id()
    {
        var p = Build(r => Task.FromResult(IsLogin(r)
            ? Json(HttpStatusCode.BadRequest, "{\"error\":\"58006\"}")
            : Json(HttpStatusCode.OK, "[]")));

        var (a, b) = await BothFail(p, (w, c) => w.GetCertificatesAsync(c, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Authentication, a.Category);
        Assert.Equal("caller-A", a.CorrelationId);
        Assert.Equal("caller-B", b.CorrelationId);

        var loginLines = p.HandlerLogger.Lines.Where(l => l.Contains("ras/v1/login", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(loginLines);
        Assert.DoesNotContain(loginLines, l => l.Contains(HandlerScopeId, StringComparison.Ordinal));
        Assert.Contains(loginLines, l => l.Contains("caller-A", StringComparison.Ordinal));
        Assert.Contains(loginLines, l => l.Contains("caller-B", StringComparison.Ordinal));
    }
}
