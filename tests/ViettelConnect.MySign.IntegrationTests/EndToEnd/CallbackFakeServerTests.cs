using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Application.UseCases;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Domain.Signing;
using ViettelConnect.MySign.Infrastructure.Callback;
using ViettelConnect.MySign.Infrastructure.Certificates;
using ViettelConnect.MySign.Infrastructure.Signing;
using ViettelConnect.MySign.IntegrationTests.Fake;
using ViettelConnect.MySign.IntegrationTests.Http;
using Xunit;

namespace ViettelConnect.MySign.IntegrationTests.EndToEnd;

public class CallbackFakeServerTests
{
    private static readonly MySignCredentials Creds = new("client-A", "secret-A", "profile-1", "user-1");

    private sealed class FixedCredentials : IMySignCredentialsAccessor
    {
        public MySignCredentials Get() => Creds;
    }

    private sealed class FixedCorrelation : ICorrelationIdAccessor
    {
        public string Current => "corr-test";
    }

    private sealed class Rig
    {
        public Rig(FakeMySignServer fake)
        {
            var pipeline = TestPipeline.Create(fake.BaseUrl);
            Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
            Pending = new InMemoryPendingSignatureStore();
            Tokens = new InMemoryCallbackTokenStore(Time);
            var corr = new FixedCorrelation();
            Begin = new BeginSignHashes(
                pipeline.Wire,
                new FixedCredentials(),
                new FirstUsableCertificateSelector(Time, corr),
                new MobileConfirmationAuthorizationProvider(),
                corr,
                Pending,
                Time);
            Token = new HandleCallbackTokenRequest(
                new FixedResolver(),
                Tokens,
                TimeSpan.FromHours(1),
                Time);
            Result = new HandleCallbackResult(Tokens, Pending);
        }

        public FakeTimeProvider Time { get; }

        public InMemoryPendingSignatureStore Pending { get; }

        public InMemoryCallbackTokenStore Tokens { get; }

        public BeginSignHashes Begin { get; }

        public HandleCallbackTokenRequest Token { get; }

        public HandleCallbackResult Result { get; }

        public async Task<string> TokenFor(string clientId, string secret)
        {
            var body = JsonSerializer.Serialize(new { grant_type = "client_credentials", client_id = clientId, client_secret = secret });
            var response = await Token.ExecuteAsync(body, CancellationToken.None);
            using var json = JsonDocument.Parse(response.JsonBody);
            return json.RootElement.GetProperty("data").GetProperty("access_token").GetString()!;
        }
    }

    private sealed class FixedResolver : ICallbackClientResolver
    {
        public ValueTask<CallbackClient?> FindAsync(string clientId, CancellationToken ct) =>
            ValueTask.FromResult<CallbackClient?>(clientId switch
            {
                "cb-a" => new CallbackClient("labA", "cb-a", "secret-a"),
                "cb-b" => new CallbackClient("labB", "cb-b", "secret-b"),
                _ => null,
            });
    }

    private sealed class FailingPendingStore : IPendingSignatureStore
    {
        public ValueTask SaveAsync(PendingSignature p, CancellationToken ct) => throw new InvalidOperationException("store down");

        public ValueTask<PendingSignature?> FindAsync(string tenantKey, string transactionId, CancellationToken ct) =>
            ValueTask.FromResult<PendingSignature?>(null);

        public ValueTask<bool> TryRemoveAsync(string tenantKey, string transactionId, CancellationToken ct) =>
            ValueTask.FromResult(false);
    }

    [Fact]
    public async Task Begin_store_failure_after_submission_carries_transaction_id()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var pipeline = TestPipeline.Create(fake.BaseUrl);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var corr = new FixedCorrelation();
        var begin = new BeginSignHashes(
            pipeline.Wire,
            new FixedCredentials(),
            new FirstUsableCertificateSelector(time, corr),
            new MobileConfirmationAuthorizationProvider(),
            corr,
            new FailingPendingStore(),
            time);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => begin.ExecuteAsync(new SignHashesRequest([Doc("a")], null, null), "labA", CancellationToken.None));

        Assert.Equal("tx-1", ex.TransactionId);
        Assert.Equal("corr-test", ex.CorrelationId);
        Assert.DoesNotContain("store down", ex.Detail);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    private static HashDocument Doc(string id) =>
        new(id, "Biên bản", SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    /// <summary>
    /// Ngắt dòng base64 bằng CRLF (xuống dòng kiểu MIME) tường minh (độ rộng nhỏ để chữ ký ngắn của fake cũng bị ngắt), nên nhánh CRLF luôn
    /// được chạy bất kể hệ điều hành; <see cref="ResultBody"/> được test khẳng định có CRLF.
    /// </summary>
    private static string WithCrlf(string base64)
    {
        const int width = 4;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < base64.Length; i += width)
        {
            if (i > 0)
            {
                sb.Append("\r\n");
            }

            sb.Append(base64, i, Math.Min(width, base64.Length - i));
        }

        return sb.ToString();
    }

    private static string ResultBody(string tx, string status, params (string Id, byte[] Signature)[] results) =>
        JsonSerializer.Serialize(new
        {
            meta_data = new { request_id = "VIETTEL_" + tx, transaction_id = tx, description = "Ký thành công", status },
            data = new
            {
                action_timestamp = 1735543766,
                signature_timestamp = 1735543766,
                results = results
                    .Select(r => new { document_id = r.Id, signature = WithCrlf(Convert.ToBase64String(r.Signature)) })
                    .ToArray(),
            },
        });

    private static string StatusCode(CallbackResponse r)
    {
        using var json = JsonDocument.Parse(r.JsonBody);
        return json.RootElement.GetProperty("status_code").GetString()!;
    }

    [Fact]
    public async Task Begin_sends_async_three_saves_pending_and_callback_completes_it()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var rig = new Rig(fake);
        var docs = new[] { Doc("a"), Doc("b") };

        var begun = await rig.Begin.ExecuteAsync(new SignHashesRequest(docs, null, null), "labA", CancellationToken.None);

        using (var sent = Assert.Single(fake.RequestsFor("signHash")).Json())
        {
            Assert.Equal(3, sent.RootElement.GetProperty("async").GetInt32());
        }

        Assert.Equal("tx-1", begun.TransactionId);
        Assert.Equal("cred-001", begun.Certificate.CredentialId);
        var pending = await rig.Pending.FindAsync("labA", "tx-1", CancellationToken.None);
        Assert.NotNull(pending);
        Assert.Equal(["a", "b"], pending.DocumentIds);
        Assert.Equal(rig.Time.GetUtcNow(), pending.CreatedAt);

        var token = await rig.TokenFor("cb-a", "secret-a");
        var body = ResultBody(
            begun.TransactionId,
            "1",
            ("b", FakeMySignServer.SignatureFor("b")),
            ("a", FakeMySignServer.SignatureFor("a")));
        Assert.Contains("\\r\\n", body, StringComparison.Ordinal); // JSON-escaped CRLF thật sự có trong thân
        var (response, outcome) = await rig.Result.ExecuteAsync(token, body, CancellationToken.None);

        Assert.Equal("00", StatusCode(response));
        Assert.NotNull(outcome);
        Assert.Equal(SignStatus.Succeeded, outcome.Status);
        Assert.Equal(["a", "b"], outcome.Signatures.Select(s => s.DocumentId));
        Assert.Equal(FakeMySignServer.SignatureFor("a"), outcome.Signatures[0].Signature);
        Assert.Equal(FakeMySignServer.SignatureFor("b"), outcome.Signatures[1].Signature);
        Assert.Null(await rig.Pending.FindAsync("labA", "tx-1", CancellationToken.None));
    }

    [Fact]
    public async Task Two_labs_are_isolated_end_to_end()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var rig = new Rig(fake);
        var a = await rig.Begin.ExecuteAsync(new SignHashesRequest([Doc("a")], null, null), "labA", CancellationToken.None);
        var b = await rig.Begin.ExecuteAsync(new SignHashesRequest([Doc("a")], null, null), "labB", CancellationToken.None);
        var tokenA = await rig.TokenFor("cb-a", "secret-a");

        var (cross, crossOutcome) = await rig.Result.ExecuteAsync(
            tokenA, ResultBody(b.TransactionId, "1", ("a", FakeMySignServer.SignatureFor("a"))), CancellationToken.None);
        var (own, ownOutcome) = await rig.Result.ExecuteAsync(
            tokenA, ResultBody(a.TransactionId, "1", ("a", FakeMySignServer.SignatureFor("a"))), CancellationToken.None);

        Assert.NotEqual("00", StatusCode(cross));
        Assert.Null(crossOutcome);
        Assert.Equal("00", StatusCode(own));
        Assert.Equal("labA", ownOutcome!.TenantKey);
        Assert.NotNull(await rig.Pending.FindAsync("labB", b.TransactionId, CancellationToken.None));
    }

    [Fact]
    public async Task Rejected_callback_yields_rejected_outcome()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var rig = new Rig(fake);
        var begun = await rig.Begin.ExecuteAsync(new SignHashesRequest([Doc("a")], null, null), "labA", CancellationToken.None);
        var token = await rig.TokenFor("cb-a", "secret-a");

        var (response, outcome) = await rig.Result.ExecuteAsync(token, ResultBody(begun.TransactionId, "4002"), CancellationToken.None);

        Assert.Equal("00", StatusCode(response));
        Assert.Equal(SignStatus.Rejected, outcome!.Status);
        Assert.Empty(outcome.Signatures);
    }

    [Fact]
    public async Task Missing_transaction_id_is_a_protocol_violation_and_nothing_is_saved()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.OmitTransactionId = true;
        var rig = new Rig(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => rig.Begin.ExecuteAsync(new SignHashesRequest([Doc("a")], null, null), "labA", CancellationToken.None));

        Assert.Equal(MySignErrorCategory.ProtocolViolation, ex.Category);
        Assert.Equal("corr-test", ex.CorrelationId);
    }

    [Fact]
    public async Task Begin_reuses_signing_validation()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var rig = new Rig(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => rig.Begin.ExecuteAsync(new SignHashesRequest([], null, null), "labA", CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Validation, ex.Category);
        Assert.Empty(fake.RequestsFor("signHash"));
    }

    [Fact]
    public async Task Begin_requires_tenant_key()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var rig = new Rig(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => rig.Begin.ExecuteAsync(new SignHashesRequest([Doc("a")], null, null), " ", CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Validation, ex.Category);
        Assert.Empty(fake.RequestsFor("signHash"));
    }
}
