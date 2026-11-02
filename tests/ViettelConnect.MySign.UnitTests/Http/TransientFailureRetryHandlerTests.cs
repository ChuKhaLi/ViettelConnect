using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Infrastructure.Configuration;
using ViettelConnect.MySign.Infrastructure.Http;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Http;

public sealed class TransientFailureRetryHandlerTests
{
    private sealed class FixedCorrelation : ICorrelationIdAccessor
    {
        public string Current => "corr-test";
    }

    /// <summary>Nội dung ghi nhận việc bị dispose (để kiểm chứng phản hồi 5xx không bị rò rỉ).</summary>
    private sealed class TrackingContent : HttpContent
    {
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class Always503 : HttpMessageHandler
    {
        public List<TrackingContent> Contents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new TrackingContent();
            Contents.Add(content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = content });
        }
    }

    private sealed class ThrowingDelayer(Exception error) : IDelayer
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.FromException(error);
    }

    private static HttpMessageInvoker Build(Always503 terminal, IDelayer delayer)
    {
        var options = Options.Create(new MySignOptions { TransportRetry = new TransportRetryOptions { MaxAttempts = 3 } });
        var time = new FakeTimeProvider();
        var handler = new TransientFailureRetryHandler(options, delayer, new FixedCorrelation(), time, _ => 0)
        {
            InnerHandler = terminal,
        };
        return new HttpMessageInvoker(handler);
    }

    private static HttpRequestMessage Request() =>
        new(HttpMethod.Post, "http://127.0.0.1:1/vtss/service/certificates/info") { Content = new StringContent("{}") };

    [Fact]
    public async Task Last_transient_response_is_disposed_when_the_backoff_delay_is_cancelled()
    {
        var terminal = new Always503();
        using var invoker = Build(terminal, new ThrowingDelayer(new OperationCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoker.SendAsync(Request(), CancellationToken.None));

        var content = Assert.Single(terminal.Contents);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task Last_transient_response_is_disposed_when_the_delay_throws_any_exception()
    {
        var terminal = new Always503();
        using var invoker = Build(terminal, new ThrowingDelayer(new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(Request(), CancellationToken.None));

        Assert.True(Assert.Single(terminal.Contents).Disposed);
    }

    [Fact]
    public async Task Exhausted_retries_return_the_last_response_undisposed_and_dispose_earlier_ones()
    {
        var terminal = new Always503();
        using var invoker = Build(terminal, new NoDelay());

        using var response = await invoker.SendAsync(Request(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, terminal.Contents.Count);
        Assert.True(terminal.Contents[0].Disposed);
        Assert.True(terminal.Contents[1].Disposed);
        Assert.False(terminal.Contents[2].Disposed);
    }

    private sealed class NoDelay : IDelayer
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
    }
}
