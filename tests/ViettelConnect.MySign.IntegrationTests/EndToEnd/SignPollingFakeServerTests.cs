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

    [Fact]
    public async Task Transient_status_poll_failures_are_treated_as_pending()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        // TestPipeline thử 3 lần mỗi lời gọi: 6 lần 503 = hai lần hỏi trạng thái thất bại (Transport) rồi "1".
        fake.Scenario.FailTimesByRoute["requests/status"] = (503, 6);
        var (sign, delayer) = Build(fake);

        var result = await sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None);

        Assert.Equal(FirstTx, result.TransactionId);
        Assert.Single(result.Signatures);
        Assert.Equal(7, fake.RequestsFor("requests/status").Count);
        Assert.Equal(2, delayer.Calls);
    }

    [Fact]
    public async Task Status_endpoint_down_until_deadline_ends_in_timeout_with_transaction_id()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["requests/status"] = (503, string.Empty);
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignTimeoutException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));

        Assert.Equal(FirstTx, ex.TransactionId);
        Assert.Equal("corr-test", ex.CorrelationId);
    }

    [Fact]
    public async Task Final_timeout_carries_the_last_raw_status()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.StatusSequence[FirstTx] = ["4000", "4999"];
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignTimeoutException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));

        Assert.Equal("4999", ex.RawCode);
        Assert.Equal(FirstTx, ex.TransactionId);
    }

    [Fact]
    public async Task Post_submission_rejection_carries_transaction_id()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.StatusSequence[FirstTx] = ["4000", "4002"];
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.SignRejected, ex.Category);
        Assert.Equal(FirstTx, ex.TransactionId);
    }

    [Fact]
    public async Task Post_submission_failure_and_protocol_violation_carry_transaction_id()
    {
        await using (var fake = await FakeMySignServer.StartAsync())
        {
            fake.Scenario.StatusSequence[FirstTx] = ["4004"];
            var (sign, _) = Build(fake);

            var failed = await Assert.ThrowsAsync<MySignException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));

            Assert.Equal(MySignErrorCategory.SignFailed, failed.Category);
            Assert.Equal(FirstTx, failed.TransactionId);
        }

        await using (var fake = await FakeMySignServer.StartAsync())
        {
            fake.Scenario.DropLastSignature = true;
            var (sign, _) = Build(fake);

            var protocol = await Assert.ThrowsAsync<MySignException>(
                () => sign.ExecuteAsync(Request(Doc("a"), Doc("b")), CancellationToken.None));

            Assert.Equal(MySignErrorCategory.ProtocolViolation, protocol.Category);
            Assert.Equal(FirstTx, protocol.TransactionId);
        }
    }

    [Fact]
    public async Task Non_base64_signature_in_status_is_a_protocol_violation_carrying_transaction_id()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["requests/status"] = (200, "{\"status\":\"1\",\"signatures\":[\"!!not-base64!!\"]}");
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.ProtocolViolation, ex.Category);
        Assert.Equal(FirstTx, ex.TransactionId);
        Assert.Equal("corr-test", ex.CorrelationId);
    }

    [Fact]
    public async Task Status_endpoint_58061_error_is_a_timeout_exception_carrying_transaction_id()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["requests/status"] =
            (400, "{\"error\":\"58061\",\"error_description\":\"Request timeout for mobile authorisation\"}");
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignTimeoutException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.SignTimeout, ex.Category);
        Assert.Equal("58061", ex.RawCode);
        Assert.Equal(FirstTx, ex.TransactionId);
        Assert.Equal("corr-test", ex.CorrelationId);
    }

    /// <summary>Dựng với đồng hồ và delayer thật (cho các test giới hạn thời gian tổng thật sự).</summary>
    private static SignHashes BuildRealTime(FakeMySignServer fake, TimeSpan interval, TimeSpan total)
    {
        foreach (var cert in fake.Scenario.Certificates)
        {
            cert.ValidTo = "20301231000000Z";
        }

        var pipeline = TestPipeline.Create(fake.BaseUrl);
        var corr = new FixedCorrelation();
        return new SignHashes(
            pipeline.Wire,
            new FixedCredentials(),
            new FirstUsableCertificateSelector(TimeProvider.System, corr),
            new MobileConfirmationAuthorizationProvider(),
            corr,
            new PollSignStatus(pipeline.Wire, new ViettelConnect.MySign.Infrastructure.Time.TaskDelayer(), TimeProvider.System, corr),
            new MySignSigningSettings(SignMode.Polling, 2, interval, total));
    }

    [Fact]
    public async Task Hanging_status_call_is_bounded_by_total_timeout()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.StatusSequence[FirstTx] = ["4000"];
        fake.Scenario.DelayByRoute["requests/status"] = TimeSpan.FromSeconds(30);
        var total = TimeSpan.FromSeconds(1.5);
        var sign = BuildRealTime(fake, TimeSpan.FromMilliseconds(100), total);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<MySignTimeoutException>(() => sign.ExecuteAsync(Request(Doc("a")), CancellationToken.None));
        sw.Stop();

        Assert.Equal(FirstTx, ex.TransactionId);
        Assert.Equal("corr-test", ex.CorrelationId);
        Assert.InRange(sw.Elapsed, total - TimeSpan.FromMilliseconds(200), total + TimeSpan.FromSeconds(3));
    }

    /// <summary>Wire giả cho PollSignStatus: lần hỏi đầu trả "4000", các lần sau treo tới khi bị huỷ.</summary>
    private sealed class PendingThenHangingWire : IMySignWireClient
    {
        private int _calls;

        public TaskCompletionSource SecondCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AccessToken> LoginAsync(MySignCredentials c, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<ViettelConnect.MySign.Domain.Certificates.Certificate>> GetCertificatesAsync(
            MySignCredentials c, CancellationToken ct) => throw new NotSupportedException();

        public Task<SignHashWireResponse> SignHashAsync(MySignCredentials c, SignHashWireRequest r, CancellationToken ct) =>
            throw new NotSupportedException();

        public async Task<StatusSnapshot> GetStatusAsync(MySignCredentials c, string transactionId, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return new StatusSnapshot("4000", SignStatus.Pending, [], null, null);
            }

            SecondCallStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    [Fact]
    public async Task Hanging_status_call_ends_at_deadline_with_last_raw_status()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var wire = new PendingThenHangingWire();
        var poll = new PollSignStatus(wire, new AdvancingDelayer(time), time, new FixedCorrelation());

        var task = poll.ExecuteAsync(Creds, FirstTx, Interval, Total, CancellationToken.None);
        await wire.SecondCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(task.IsCompleted);

        // Lần hỏi thứ hai bắt đầu ở t=5s; ngân sách còn 55s. Trước hạn chót: vẫn treo; tới hạn chót: hết giờ.
        time.Advance(TimeSpan.FromSeconds(54));
        Assert.False(task.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<MySignTimeoutException>(() => task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("4000", ex.RawCode);
        Assert.Equal(FirstTx, ex.TransactionId);
        Assert.Equal("corr-test", ex.CorrelationId);
        Assert.Equal(time.GetUtcNow(), new DateTimeOffset(2026, 11, 2, 8, 1, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Caller_cancellation_during_hanging_status_call_is_operation_canceled()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.DelayByRoute["requests/status"] = TimeSpan.FromSeconds(30);
        var sign = BuildRealTime(fake, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(20));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sign.ExecuteAsync(Request(Doc("a")), cts.Token));

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }
}
