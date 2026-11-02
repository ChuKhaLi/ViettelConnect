using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Application.UseCases;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Domain.Signing;
using ViettelConnect.MySign.Infrastructure.Certificates;
using ViettelConnect.MySign.Infrastructure.Signing;
using ViettelConnect.MySign.IntegrationTests.Fake;
using ViettelConnect.MySign.IntegrationTests.Http;
using Xunit;

namespace ViettelConnect.MySign.IntegrationTests.EndToEnd;

public class SignPollingFakeServerTests
{
    private const string FirstTx = "tx-1";
    private static readonly MySignCredentials Creds = new("client-A", "secret-A", "profile-1", "user-1");
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Total = TimeSpan.FromSeconds(60);

    private sealed class FixedCredentials : IMySignCredentialsAccessor
    {
        public MySignCredentials Get() => Creds;
    }

    private sealed class FixedCorrelation : ICorrelationIdAccessor
    {
        public string Current => "corr-test";
    }

    /// <summary>Delayer giả: không chờ thật, chỉ đẩy FakeTimeProvider đi đúng khoảng được yêu cầu.</summary>
    private sealed class AdvancingDelayer(FakeTimeProvider time) : IDelayer
    {
        public int Calls { get; private set; }

        public Action? OnDelay { get; set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            Calls++;
            OnDelay?.Invoke();
            ct.ThrowIfCancellationRequested();
            time.Advance(delay);
            return Task.CompletedTask;
        }
    }

    private static (SignHashes Sign, AdvancingDelayer Delayer) Build(FakeMySignServer fake, int pollingAsyncCode = 2)
    {
        foreach (var cert in fake.Scenario.Certificates)
        {
            cert.ValidTo = "20301231000000Z";
        }

        var pipeline = TestPipeline.Create(fake.BaseUrl);
        var corr = new FixedCorrelation();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var delayer = new AdvancingDelayer(time);
        var sign = new SignHashes(
            pipeline.Wire,
            new FixedCredentials(),
            new FirstUsableCertificateSelector(time, corr),
            new MobileConfirmationAuthorizationProvider(),
            corr,
            new PollSignStatus(pipeline.Wire, delayer, time, corr),
            new MySignSigningSettings(SignMode.Polling, pollingAsyncCode, Interval, Total));
        return (sign, delayer);
    }

    private static HashDocument Doc(string id) =>
        new(id, "Biên bản", SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    private static SignHashesRequest Request(params HashDocument[] docs) => new(docs, null, null);

    [Fact]
    public async Task Pending_pending_then_success_polls_three_times_and_returns_transaction_id()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.StatusSequence[FirstTx] = ["4000", "6000", "1"];
        var (sign, delayer) = Build(fake);
        var docs = new[] { Doc("a"), Doc("b") };

        var result = await sign.ExecuteAsync(Request(docs), CancellationToken.None);

        Assert.Equal(3, fake.RequestsFor("requests/status").Count);
        Assert.Equal(2, delayer.Calls);
        Assert.Equal(FirstTx, result.TransactionId);
        Assert.Equal(docs.Select(d => d.DocumentId), result.Signatures.Select(s => s.DocumentId));
        for (var i = 0; i < docs.Length; i++)
        {
            Assert.Equal(
                FakeMySignServer.SignatureFor(Convert.ToBase64String(docs[i].Sha256)),
                result.Signatures[i].Signature);
        }
    }

    [Fact]
    public async Task Default_polling_code_two_is_sent_as_async()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var (sign, _) = Build(fake);

        await sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None);

        using var json = Assert.Single(fake.RequestsFor("signHash")).Json();
        Assert.Equal(2, json.RootElement.GetProperty("async").GetInt32());
    }

    [Fact]
    public async Task Configured_polling_code_one_is_sent_as_async()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var (sign, _) = Build(fake, pollingAsyncCode: 1);

        await sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None);

        using var json = Assert.Single(fake.RequestsFor("signHash")).Json();
        Assert.Equal(1, json.RootElement.GetProperty("async").GetInt32());
    }

    [Fact]
    public async Task Rejected_status_maps_to_sign_rejected()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.StatusSequence[FirstTx] = ["4000", "4002"];
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.SignRejected, ex.Category);
        Assert.Equal("corr-test", ex.CorrelationId);
    }

    [Fact]
    public async Task Timed_out_status_throws_timeout_exception_with_transaction_id()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.StatusSequence[FirstTx] = ["4001"];
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignTimeoutException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));

        Assert.Equal(FirstTx, ex.TransactionId);
        Assert.Equal("corr-test", ex.CorrelationId);
    }

    [Theory]
    [InlineData("4005", MySignErrorCategory.InsufficientQuota)]
    [InlineData("4006", MySignErrorCategory.CertificateInvalid)]
    [InlineData("13003", MySignErrorCategory.CertificateInvalid)]
    [InlineData("13004", MySignErrorCategory.CertificateInvalid)]
    [InlineData("4004", MySignErrorCategory.SignFailed)]
    [InlineData("50000", MySignErrorCategory.SignFailed)]
    public async Task Failed_status_maps_to_category_by_raw_code(string status, MySignErrorCategory expected)
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.StatusSequence[FirstTx] = [status];
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));

        Assert.Equal(expected, ex.Category);
        Assert.Equal(status, ex.RawCode);
        Assert.Equal("corr-test", ex.CorrelationId);
    }

    [Theory]
    [InlineData("4999")]
    [InlineData("")]
    public async Task Unknown_or_empty_status_keeps_polling_until_total_timeout(string status)
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.StatusSequence[FirstTx] = [status];
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignTimeoutException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));

        // 60s / 5s = 12 khoảng chờ => 13 lần hỏi (lần đầu ngay lúc t=0 và lần cuối đúng hạn chót).
        Assert.Equal(13, fake.RequestsFor("requests/status").Count);
        Assert.Equal(FirstTx, ex.TransactionId);
    }

    [Fact]
    public async Task Success_with_fewer_signatures_than_hashes_is_a_protocol_violation()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.DropLastSignature = true;
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => sign.ExecuteAsync(Request(Doc("a"), Doc("b"), Doc("c")), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.ProtocolViolation, ex.Category);
        Assert.Equal("corr-test", ex.CorrelationId);
    }

    [Fact]
    public async Task Missing_transaction_id_is_a_protocol_violation_and_nothing_is_polled()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.OmitTransactionId = true;
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.ProtocolViolation, ex.Category);
        Assert.Empty(fake.RequestsFor("requests/status"));
    }

    [Fact]
    public async Task Cancellation_between_polls_stops_further_polling()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.StatusSequence[FirstTx] = ["4000"];
        var (sign, delayer) = Build(fake);
        using var cts = new CancellationTokenSource();
        delayer.OnDelay = cts.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sign.ExecuteAsync(Request(Doc("a")), cts.Token));

        Assert.Single(fake.RequestsFor("requests/status"));
    }
}
