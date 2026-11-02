using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Infrastructure.Caching;
using ViettelConnect.MySign.Infrastructure.Configuration;
using ViettelConnect.MySign.Infrastructure.Http;
using ViettelConnect.MySign.Infrastructure.Wire;

namespace ViettelConnect.MySign.IntegrationTests.Http;

/// <summary>Dựng tay pipeline HttpClient (retry -> auth -> mạng) cho test; DI thật nằm ở Task 10.</summary>
internal sealed class TestPipeline
{
    private sealed class FixedCorrelation : ICorrelationIdAccessor
    {
        public string Current => "corr-test";
    }

    private sealed class NoDelay : IDelayer
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
    }

    private TestPipeline(IMySignWireClient wire, InMemoryTokenCache cache, FakeTimeProvider time, SingleFlightRefresh singleFlight)
    {
        Wire = wire;
        Cache = cache;
        Time = time;
        SingleFlight = singleFlight;
    }

    public IMySignWireClient Wire { get; }

    public InMemoryTokenCache Cache { get; }

    public FakeTimeProvider Time { get; }

    public SingleFlightRefresh SingleFlight { get; }

    public static TestPipeline Create(
        string baseUrl,
        HttpMessageHandler? terminal = null,
        int maxAttempts = 3,
        ILogger<MySignAuthHandler>? authLogger = null)
    {
        var options = Options.Create(new MySignOptions
        {
            BaseUrl = baseUrl,
            TransportRetry = new TransportRetryOptions { MaxAttempts = maxAttempts },
        });
        var correlation = new FixedCorrelation();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var cache = new InMemoryTokenCache();
        var singleFlight = new SingleFlightRefresh();

        IMySignWireClient? wire = null;

        // Func<> tránh vòng phụ thuộc handler -> wire client -> HttpClient -> handler.
        var auth = new MySignAuthHandler(() => wire!, cache, singleFlight, time, correlation, authLogger)
        {
            InnerHandler = terminal ?? new HttpClientHandler(),
        };
        var retry = new TransientFailureRetryHandler(options, new NoDelay(), correlation, time) { InnerHandler = auth };

        wire = new MySignWireClient(new HttpClient(retry), options, correlation, time);
        return new TestPipeline(wire, cache, time, singleFlight);
    }
}
