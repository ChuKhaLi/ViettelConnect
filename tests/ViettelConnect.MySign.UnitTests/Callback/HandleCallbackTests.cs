using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Application.UseCases;
using ViettelConnect.MySign.Domain.Certificates;
using ViettelConnect.MySign.Domain.Signing;
using ViettelConnect.MySign.Infrastructure.Callback;
using ViettelConnect.MySign.Infrastructure.Configuration;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Callback;

public sealed class HandleCallbackTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private sealed class ListResolver(params CallbackClient[] clients) : ICallbackClientResolver
    {
        public ValueTask<CallbackClient?> FindAsync(string clientId, CancellationToken ct) =>
            ValueTask.FromResult(clients.FirstOrDefault(c => c.ClientId == clientId));
    }

    private sealed class Harness
    {
        public Harness()
        {
            Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
            Tokens = new InMemoryCallbackTokenStore(Time);
            Pending = new InMemoryPendingSignatureStore();
            Token = new HandleCallbackTokenRequest(
                new ListResolver(
                    new CallbackClient("labA", "cb-a", "secret-a"),
                    new CallbackClient("labB", "cb-b", "secret-b")),
                Tokens,
                Lifetime,
                Time);
            Result = new HandleCallbackResult(Tokens, Pending);
        }

        public FakeTimeProvider Time { get; }

        public InMemoryCallbackTokenStore Tokens { get; }

        public InMemoryPendingSignatureStore Pending { get; }

        public HandleCallbackTokenRequest Token { get; }

        public HandleCallbackResult Result { get; }

        public async Task<string> TokenFor(string clientId, string secret)
        {
            var response = await Token.ExecuteAsync(TokenBody(clientId, secret), CancellationToken.None);
            using var json = JsonDocument.Parse(response.JsonBody);
            Assert.Equal("00", json.RootElement.GetProperty("status_code").GetString());
            return json.RootElement.GetProperty("data").GetProperty("access_token").GetString()!;
        }

        public Task Seed(string tenant, string tx, params string[] documentIds) =>
            Pending.SaveAsync(
                new PendingSignature(tx, tenant, Cert(), documentIds, Time.GetUtcNow()),
                CancellationToken.None).AsTask();
    }

    private static Certificate Cert() =>
        new("cred-1", "SN", "CN=A", "CN=CA", null, null, true, [], []);

    private static string TokenBody(string clientId, string secret, string grant = "client_credentials") =>
        JsonSerializer.Serialize(new { grant_type = grant, client_id = clientId, client_secret = secret });

    private static string Sig(string text, bool crlf = false)
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        return crlf ? b64[..6] + "\r\n" + b64[6..12] + "\r\n" + b64[12..] : b64;
    }

    /// <summary>"2" -> số JSON 2; "'2" -> chuỗi "2"; còn lại giữ nguyên chuỗi (id lạ).</summary>
    private static object WireId(string id) =>
        id.StartsWith('\'') ? id[1..] : int.TryParse(id, out var n) ? n : id;

    private static string ResultBody(string tx, string status, params (string Id, string Signature)[] results) =>
        JsonSerializer.Serialize(new
        {
            meta_data = new { request_id = "VIETTEL_" + tx, transaction_id = tx, description = "x", status },
            data = new
            {
                action_timestamp = 1735543766,
                signature_timestamp = 1735543766,
                results = results.Select(r => new { document_id = WireId(r.Id), signature = r.Signature }).ToArray(),
            },
        });

    private static string StatusCode(CallbackResponse r)
    {
        using var json = JsonDocument.Parse(r.JsonBody);
        return json.RootElement.GetProperty("status_code").GetString()!;
    }

    // ---- token request ----

    [Fact]
    public async Task Token_request_with_valid_client_returns_viettel_shaped_response()
    {
        var h = new Harness();

        var response = await h.Token.ExecuteAsync(TokenBody("cb-a", "secret-a"), CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        using var json = JsonDocument.Parse(response.JsonBody);
        var root = json.RootElement;
        Assert.Equal("00", root.GetProperty("status_code").GetString());
        Assert.Equal("Thành công", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.True(data.GetProperty("access_token").GetString()!.Length >= 43);
        Assert.Equal("600000", data.GetProperty("expires_in").GetString());
        Assert.Equal(h.Time.GetUtcNow().ToUnixTimeSeconds().ToString(), data.GetProperty("consented_on").GetString());
    }

    [Theory]
    [InlineData("cb-a", "wrong")]
    [InlineData("unknown", "secret-a")]
    [InlineData("", "")]
    public async Task Token_request_with_bad_credentials_returns_generic_failure(string id, string secret)
    {
        var h = new Harness();

        var response = await h.Token.ExecuteAsync(TokenBody(id, secret), CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        using var json = JsonDocument.Parse(response.JsonBody);
        Assert.Equal("01", json.RootElement.GetProperty("status_code").GetString());
        Assert.Equal("invalid_client", json.RootElement.GetProperty("message").GetString());
        Assert.False(json.RootElement.TryGetProperty("data", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"client_id\":5}")]
    public async Task Token_request_with_malformed_body_does_not_throw(string body)
    {
        var h = new Harness();

        var response = await h.Token.ExecuteAsync(body, CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        Assert.NotEqual("00", StatusCode(response));
    }

    [Theory]
    [InlineData("{\"client_id\":\"cb-a\",\"client_secret\":\"secret-a\"}")]
    [InlineData("{\"grant_type\":null,\"client_id\":\"cb-a\",\"client_secret\":\"secret-a\"}")]
    [InlineData("{\"grant_type\":\"\",\"client_id\":\"cb-a\",\"client_secret\":\"secret-a\"}")]
    [InlineData("{\"grant_type\":\"password\",\"client_id\":\"cb-a\",\"client_secret\":\"secret-a\"}")]
    [InlineData("{\"grant_type\":\"Client_Credentials\",\"client_id\":\"cb-a\",\"client_secret\":\"secret-a\"}")]
    public async Task Token_request_requires_client_credentials_grant_type(string body)
    {
        var h = new Harness();

        var response = await h.Token.ExecuteAsync(body, CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        using var json = JsonDocument.Parse(response.JsonBody);
        Assert.Equal("01", json.RootElement.GetProperty("status_code").GetString());
        Assert.Equal("invalid_client", json.RootElement.GetProperty("message").GetString());
        Assert.False(json.RootElement.TryGetProperty("data", out _));
    }

    // ---- store failures (A11) ----

    private const string StoreSecretDetail = "db-password=hunter2 at Server=sql01";

    private sealed class ThrowingTokenStore(bool throwOnIssue, bool throwOnValidate) : ICallbackTokenStore
    {
        public ValueTask<string> IssueAsync(string tenantKey, TimeSpan lifetime, CancellationToken ct) =>
            throwOnIssue ? throw new InvalidOperationException(StoreSecretDetail) : ValueTask.FromResult("tok-" + tenantKey);

        public ValueTask<string?> ValidateAsync(string token, CancellationToken ct) =>
            throwOnValidate ? throw new InvalidOperationException(StoreSecretDetail) : ValueTask.FromResult<string?>("labA");
    }

    private sealed class ThrowingPendingStore(string failOn) : IPendingSignatureStore
    {
        public ValueTask SaveAsync(PendingSignature p, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask<PendingSignature?> FindAsync(string tenantKey, string transactionId, CancellationToken ct) =>
            failOn == "find"
                ? throw new TimeoutException(StoreSecretDetail)
                : ValueTask.FromResult<PendingSignature?>(new PendingSignature(transactionId, tenantKey, Cert(), ["d1"], DateTimeOffset.UnixEpoch));

        public ValueTask<bool> TryRemoveAsync(string tenantKey, string transactionId, CancellationToken ct) =>
            failOn == "remove" ? throw new IOException(StoreSecretDetail) : ValueTask.FromResult(true);
    }

    private sealed class ThrowingResolver : ICallbackClientResolver
    {
        public ValueTask<CallbackClient?> FindAsync(string clientId, CancellationToken ct) =>
            throw new InvalidOperationException(StoreSecretDetail);
    }

    private static void AssertGenericServerError(CallbackResponse response)
    {
        Assert.Equal(500, response.HttpStatus);
        Assert.DoesNotContain("hunter2", response.JsonBody, StringComparison.Ordinal);
        Assert.DoesNotContain("sql01", response.JsonBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", response.JsonBody, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(response.JsonBody);
        Assert.NotEqual("00", json.RootElement.GetProperty("status_code").GetString());
        Assert.False(json.RootElement.TryGetProperty("data", out _));
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("find")]
    [InlineData("remove")]
    public async Task Throwing_store_during_result_callback_returns_generic_http_500_without_throwing(string failOn)
    {
        var tokens = new ThrowingTokenStore(throwOnIssue: false, throwOnValidate: failOn == "validate");
        var handler = new HandleCallbackResult(tokens, new ThrowingPendingStore(failOn));

        var (response, outcome) = await handler.ExecuteAsync("tok", ResultBody("tx-1", "1", ("1", Sig("s1"))), CancellationToken.None);

        AssertGenericServerError(response);
        Assert.Null(outcome);
    }

    [Fact]
    public async Task Throwing_token_store_during_token_request_returns_generic_http_500_without_throwing()
    {
        var handler = new HandleCallbackTokenRequest(
            new ListResolver(new CallbackClient("labA", "cb-a", "secret-a")),
            new ThrowingTokenStore(throwOnIssue: true, throwOnValidate: false),
            Lifetime,
            new FakeTimeProvider());

        var response = await handler.ExecuteAsync(TokenBody("cb-a", "secret-a"), CancellationToken.None);

        AssertGenericServerError(response);
    }

    [Fact]
    public async Task Throwing_client_resolver_during_token_request_returns_generic_http_500_without_throwing()
    {
        var handler = new HandleCallbackTokenRequest(
            new ThrowingResolver(),
            new ThrowingTokenStore(throwOnIssue: false, throwOnValidate: false),
            Lifetime,
            new FakeTimeProvider());

        var response = await handler.ExecuteAsync(TokenBody("cb-a", "secret-a"), CancellationToken.None);

        AssertGenericServerError(response);
    }

    [Fact]
    public async Task Caller_cancellation_is_still_propagated_from_callback_handlers()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var canceling = new CancelingTokenStore();
        var result = new HandleCallbackResult(canceling, new ThrowingPendingStore("none"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => result.ExecuteAsync("tok", ResultBody("tx-1", "1", ("1", Sig("s1"))), cts.Token));
    }

    private sealed class CancelingTokenStore : ICallbackTokenStore
    {
        public ValueTask<string> IssueAsync(string tenantKey, TimeSpan lifetime, CancellationToken ct) =>
            ValueTask.FromCanceled<string>(ct);

        public ValueTask<string?> ValidateAsync(string token, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>("labA");
        }
    }

    // ---- result: happy path ----

    [Fact]
    public async Task Valid_token_completes_pending_with_signatures_in_pending_order_and_removes_it()
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1", "d2");
        var token = await h.TokenFor("cb-a", "secret-a");

        // Viettel gửi kết quả theo thứ tự khác và chữ ký có CRLF.
        var body = ResultBody("tx-1", "1", ("2", Sig("signature-two", crlf: true)), ("1", Sig("signature-one", crlf: true)));
        var (response, outcome) = await h.Result.ExecuteAsync(token, body, CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        using var json = JsonDocument.Parse(response.JsonBody);
        Assert.Equal("00", json.RootElement.GetProperty("status_code").GetString());
        Assert.Equal("Thành công", json.RootElement.GetProperty("message").GetString());
        Assert.NotNull(outcome);
        Assert.Equal("labA", outcome.TenantKey);
        Assert.Equal("tx-1", outcome.TransactionId);
        Assert.Equal(SignStatus.Succeeded, outcome.Status);
        Assert.Equal("1", outcome.RawStatus);
        Assert.Equal(["d1", "d2"], outcome.Signatures.Select(s => s.DocumentId));
        Assert.Equal("signature-one", Encoding.UTF8.GetString(outcome.Signatures[0].Signature));
        Assert.Equal("signature-two", Encoding.UTF8.GetString(outcome.Signatures[1].Signature));
        Assert.Null(await h.Pending.FindAsync("labA", "tx-1", CancellationToken.None));
    }

    [Fact]
    public async Task Replaying_a_completed_callback_is_rejected()
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1");
        var token = await h.TokenFor("cb-a", "secret-a");
        var body = ResultBody("tx-1", "1", ("1", Sig("s")));
        await h.Result.ExecuteAsync(token, body, CancellationToken.None);

        var (response, outcome) = await h.Result.ExecuteAsync(token, body, CancellationToken.None);

        Assert.NotEqual("00", StatusCode(response));
        Assert.Null(outcome);
    }

    [Fact]
    public async Task Rejected_status_gives_rejected_outcome_without_signatures_and_removes_pending()
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1");
        var token = await h.TokenFor("cb-a", "secret-a");

        var (response, outcome) = await h.Result.ExecuteAsync(token, ResultBody("tx-1", "4002"), CancellationToken.None);

        Assert.Equal("00", StatusCode(response));
        Assert.NotNull(outcome);
        Assert.Equal(SignStatus.Rejected, outcome.Status);
        Assert.Equal("4002", outcome.RawStatus);
        Assert.Empty(outcome.Signatures);
        Assert.Null(await h.Pending.FindAsync("labA", "tx-1", CancellationToken.None));
    }

    [Fact]
    public async Task Non_final_status_is_acknowledged_but_keeps_pending()
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1");
        var token = await h.TokenFor("cb-a", "secret-a");

        var (response, outcome) = await h.Result.ExecuteAsync(token, ResultBody("tx-1", "4000"), CancellationToken.None);

        Assert.Equal("00", StatusCode(response));
        Assert.NotNull(outcome);
        Assert.Equal(SignStatus.Pending, outcome.Status);
        Assert.NotNull(await h.Pending.FindAsync("labA", "tx-1", CancellationToken.None));
    }

    // ---- result: Review Focus 5 + tenant isolation ----

    [Fact]
    public async Task Token_of_lab_a_cannot_complete_transaction_of_lab_b()
    {
        var h = new Harness();
        await h.Seed("labB", "tx-b", "d1");
        var tokenA = await h.TokenFor("cb-a", "secret-a");

        var (response, outcome) = await h.Result.ExecuteAsync(
            tokenA, ResultBody("tx-b", "1", ("1", Sig("s"))), CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        Assert.NotEqual("00", StatusCode(response));
        Assert.Null(outcome);
        Assert.NotNull(await h.Pending.FindAsync("labB", "tx-b", CancellationToken.None));

        // Token của labB thì hoàn tất được.
        var tokenB = await h.TokenFor("cb-b", "secret-b");
        var (ok, outcomeB) = await h.Result.ExecuteAsync(
            tokenB, ResultBody("tx-b", "1", ("1", Sig("s"))), CancellationToken.None);
        Assert.Equal("00", StatusCode(ok));
        Assert.Equal("labB", outcomeB!.TenantKey);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1");
        var token = await h.TokenFor("cb-a", "secret-a");
        h.Time.Advance(Lifetime + TimeSpan.FromSeconds(1));

        var (response, outcome) = await h.Result.ExecuteAsync(
            token, ResultBody("tx-1", "1", ("1", Sig("s"))), CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        Assert.NotEqual("00", StatusCode(response));
        Assert.Null(outcome);
        Assert.NotNull(await h.Pending.FindAsync("labA", "tx-1", CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage-token")]
    public async Task Missing_or_unknown_token_is_rejected_without_throwing(string? token)
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1");

        var (response, outcome) = await h.Result.ExecuteAsync(
            token, ResultBody("tx-1", "1", ("1", Sig("s"))), CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        Assert.NotEqual("00", StatusCode(response));
        Assert.Null(outcome);
    }

    [Fact]
    public async Task Unknown_transaction_is_rejected_without_detail()
    {
        var h = new Harness();
        var token = await h.TokenFor("cb-a", "secret-a");

        var (response, outcome) = await h.Result.ExecuteAsync(
            token, ResultBody("nope", "1", ("1", Sig("s"))), CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        Assert.NotEqual("00", StatusCode(response));
        Assert.Null(outcome);
        Assert.DoesNotContain("nope", response.JsonBody);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"meta_data\":{\"transaction_id\":\"tx-1\"}}")]
    public async Task Malformed_result_body_is_rejected_without_throwing(string body)
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1");
        var token = await h.TokenFor("cb-a", "secret-a");

        var (response, outcome) = await h.Result.ExecuteAsync(token, body, CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        Assert.NotEqual("00", StatusCode(response));
        Assert.Null(outcome);
    }

    // ---- result: document set verification ----

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("wrong-id")]
    [InlineData("duplicate")]
    [InlineData("bad-base64")]
    public async Task Result_document_set_must_match_pending_exactly(string scenario)
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1", "d2");
        var token = await h.TokenFor("cb-a", "secret-a");
        var results = scenario switch
        {
            "missing" => new[] { ("1", Sig("a")) },
            "extra" => new[] { ("1", Sig("a")), ("2", Sig("b")), ("3", Sig("c")) },
            "wrong-id" => new[] { ("1", Sig("a")), ("9", Sig("b")) },
            "duplicate" => new[] { ("1", Sig("a")), ("1", Sig("b")) },
            _ => new[] { ("1", Sig("a")), ("2", "***not base64***") },
        };

        var (response, outcome) = await h.Result.ExecuteAsync(
            token, ResultBody("tx-1", "1", results), CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        Assert.NotEqual("00", StatusCode(response));
        Assert.Null(outcome);
        Assert.NotNull(await h.Pending.FindAsync("labA", "tx-1", CancellationToken.None));
    }

    [Fact]
    public async Task Numeric_string_ids_map_back_to_pending_document_ids_out_of_order()
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1", "d2", "d3");
        var token = await h.TokenFor("cb-a", "secret-a");

        var body = ResultBody("tx-1", "1", ("'3", Sig("three")), ("1", Sig("one")), ("'2", Sig("two")));
        var (response, outcome) = await h.Result.ExecuteAsync(token, body, CancellationToken.None);

        Assert.Equal("00", StatusCode(response));
        Assert.NotNull(outcome);
        Assert.Equal(["d1", "d2", "d3"], outcome.Signatures.Select(s => s.DocumentId));
        Assert.Equal(["one", "two", "three"], outcome.Signatures.Select(s => Encoding.UTF8.GetString(s.Signature)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("'0")]
    [InlineData("'3")]
    [InlineData("abc")]
    [InlineData("'abc")]
    [InlineData("-1")]
    [InlineData("d1")]
    public async Task Out_of_range_or_non_numeric_wire_id_is_rejected_and_pending_kept(string badId)
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1", "d2");
        var token = await h.TokenFor("cb-a", "secret-a");

        var (response, outcome) = await h.Result.ExecuteAsync(
            token, ResultBody("tx-1", "1", ("1", Sig("a")), (badId, Sig("b"))), CancellationToken.None);

        Assert.Equal(200, response.HttpStatus);
        Assert.NotEqual("00", StatusCode(response));
        Assert.Null(outcome);
        Assert.NotNull(await h.Pending.FindAsync("labA", "tx-1", CancellationToken.None));
    }

    [Fact]
    public async Task Same_id_as_number_and_string_counts_as_duplicate()
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1", "d2");
        var token = await h.TokenFor("cb-a", "secret-a");

        var (response, outcome) = await h.Result.ExecuteAsync(
            token, ResultBody("tx-1", "1", ("1", Sig("a")), ("'1", Sig("b"))), CancellationToken.None);

        Assert.NotEqual("00", StatusCode(response));
        Assert.Null(outcome);
        Assert.NotNull(await h.Pending.FindAsync("labA", "tx-1", CancellationToken.None));
    }

    // ---- secrecy ----

    [Fact]
    public async Task ToString_of_client_outcome_and_response_never_contains_secrets()
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1");
        var tokenResponse = await h.Token.ExecuteAsync(TokenBody("cb-a", "secret-a"), CancellationToken.None);
        using var tokenJson = JsonDocument.Parse(tokenResponse.JsonBody);
        var token = tokenJson.RootElement.GetProperty("data").GetProperty("access_token").GetString()!;

        var client = new CallbackClient("labA", "cb-a", "secret-a");
        var (response, outcome) = await h.Result.ExecuteAsync(
            token, ResultBody("tx-1", "1", ("1", Sig("SECRET-SIGNATURE-BYTES"))), CancellationToken.None);

        Assert.DoesNotContain("secret-a", client.ToString());
        Assert.DoesNotContain(token, tokenResponse.ToString());
        Assert.DoesNotContain(token, response.ToString());
        Assert.DoesNotContain(Sig("SECRET-SIGNATURE-BYTES"), outcome!.ToString());
        Assert.DoesNotContain("SECRET-SIGNATURE-BYTES", outcome.ToString());
        Assert.DoesNotContain(token, outcome.ToString());
    }

    // ---- default components ----

    [Fact]
    public async Task Options_resolver_exposes_single_default_tenant_client()
    {
        var options = Options.Create(new MySignOptions
        {
            Callback = new CallbackOptions { ClientId = "cb", ClientSecret = "sec" },
        });
        var resolver = new OptionsCallbackClientResolver(options);

        var found = await resolver.FindAsync("cb", CancellationToken.None);

        Assert.Equal(new CallbackClient("default", "cb", "sec"), found);
        Assert.Null(await resolver.FindAsync("other", CancellationToken.None));
        Assert.Null(await new OptionsCallbackClientResolver(Options.Create(new MySignOptions()))
            .FindAsync(string.Empty, CancellationToken.None));
    }

    [Theory]
    [InlineData(" ", "sec")]
    [InlineData("cb", "  ")]
    [InlineData(" ", " ")]
    public async Task Options_resolver_treats_whitespace_client_as_disabled(string id, string secret)
    {
        var resolver = new OptionsCallbackClientResolver(Options.Create(new MySignOptions
        {
            Callback = new CallbackOptions { ClientId = id, ClientSecret = secret },
        }));

        Assert.Null(await resolver.FindAsync(id, CancellationToken.None));
    }

    private sealed class GatedPendingStore(IPendingSignatureStore inner) : IPendingSignatureStore
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _finds;

        public ValueTask SaveAsync(PendingSignature p, CancellationToken ct) => inner.SaveAsync(p, ct);

        public async ValueTask<PendingSignature?> FindAsync(string tenantKey, string transactionId, CancellationToken ct)
        {
            var found = await inner.FindAsync(tenantKey, transactionId, ct);

            // Cả hai lần giao đều phải qua Find trước khi bất kỳ bên nào được xoá.
            if (Interlocked.Increment(ref _finds) == 2)
            {
                _gate.SetResult();
            }

            await _gate.Task;
            return found;
        }

        public ValueTask<bool> TryRemoveAsync(string tenantKey, string transactionId, CancellationToken ct) =>
            inner.TryRemoveAsync(tenantKey, transactionId, ct);
    }

    [Fact]
    public async Task Concurrent_duplicate_delivery_yields_exactly_one_outcome_and_both_get_ok()
    {
        var h = new Harness();
        await h.Seed("labA", "tx-1", "d1");
        var token = await h.TokenFor("cb-a", "secret-a");
        var body = ResultBody("tx-1", "1", ("1", Sig("s")));
        var handler = new HandleCallbackResult(h.Tokens, new GatedPendingStore(h.Pending));

        var results = await Task.WhenAll(
            Task.Run(() => handler.ExecuteAsync(token, body, CancellationToken.None)),
            Task.Run(() => handler.ExecuteAsync(token, body, CancellationToken.None)));

        Assert.All(results, r => Assert.Equal("00", StatusCode(r.Response)));
        Assert.Single(results, r => r.Outcome is not null);
    }

    [Fact]
    public async Task Token_store_issues_distinct_long_random_urlsafe_tokens_bound_to_tenant()
    {
        var time = new FakeTimeProvider();
        var store = new InMemoryCallbackTokenStore(time);

        var t1 = await store.IssueAsync("labA", TimeSpan.FromMinutes(1), CancellationToken.None);
        var t2 = await store.IssueAsync("labB", TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.NotEqual(t1, t2);
        Assert.True(t1.Length >= 43);
        Assert.Matches("^[A-Za-z0-9_-]+$", t1);
        Assert.Equal("labA", await store.ValidateAsync(t1, CancellationToken.None));
        Assert.Equal("labB", await store.ValidateAsync(t2, CancellationToken.None));
        Assert.Null(await store.ValidateAsync(t1 + "x", CancellationToken.None));
        time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromTicks(1));
        Assert.Null(await store.ValidateAsync(t1, CancellationToken.None));
    }

    [Fact]
    public async Task Pending_store_isolates_by_tenant_and_is_thread_safe()
    {
        var store = new InMemoryPendingSignatureStore();
        await Parallel.ForEachAsync(Enumerable.Range(0, 200), async (i, ct) =>
        {
            await store.SaveAsync(new PendingSignature("tx-" + i, "labA", Cert(), ["d"], DateTimeOffset.UnixEpoch), ct);
            await store.SaveAsync(new PendingSignature("tx-" + i, "labB", Cert(), ["e"], DateTimeOffset.UnixEpoch), ct);
        });

        Assert.Equal(["d"], (await store.FindAsync("labA", "tx-7", CancellationToken.None))!.DocumentIds);
        Assert.Equal(["e"], (await store.FindAsync("labB", "tx-7", CancellationToken.None))!.DocumentIds);
        Assert.True(await store.TryRemoveAsync("labA", "tx-7", CancellationToken.None));
        Assert.False(await store.TryRemoveAsync("labA", "tx-7", CancellationToken.None));
        Assert.Null(await store.FindAsync("labA", "tx-7", CancellationToken.None));
        Assert.NotNull(await store.FindAsync("labB", "tx-7", CancellationToken.None));
    }

    private static PendingSignature Pending(string tx, string tenant = "labA") =>
        new(tx, tenant, Cert(), ["d"], DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Pending_store_expires_entries_after_the_default_24h_max_age_on_access()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var store = new InMemoryPendingSignatureStore(time, InMemoryPendingSignatureStore.DefaultMaxAge);
        Assert.Equal(TimeSpan.FromHours(24), InMemoryPendingSignatureStore.DefaultMaxAge);
        await store.SaveAsync(Pending("tx-1"), CancellationToken.None);

        time.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        Assert.NotNull(await store.FindAsync("labA", "tx-1", CancellationToken.None));

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(await store.FindAsync("labA", "tx-1", CancellationToken.None));
        Assert.False(await store.TryRemoveAsync("labA", "tx-1", CancellationToken.None));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task Pending_store_sweeps_expired_entries_periodically_without_access()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var store = new InMemoryPendingSignatureStore(time, TimeSpan.FromHours(1));
        await store.SaveAsync(Pending("old-1"), CancellationToken.None);
        await store.SaveAsync(Pending("old-2", "labB"), CancellationToken.None);

        time.Advance(TimeSpan.FromHours(2));
        await store.SaveAsync(Pending("new-1"), CancellationToken.None);

        Assert.Equal(1, store.Count);
        Assert.NotNull(await store.FindAsync("labA", "new-1", CancellationToken.None));
    }

    [Fact]
    public async Task Pending_store_respects_a_configured_max_age()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var store = new InMemoryPendingSignatureStore(time, TimeSpan.FromMinutes(10));
        await store.SaveAsync(Pending("tx-1"), CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromTicks(1));

        Assert.Null(await store.FindAsync("labA", "tx-1", CancellationToken.None));
    }

    [Fact]
    public void Pending_store_rejects_non_positive_max_age()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryPendingSignatureStore(new FakeTimeProvider(), TimeSpan.Zero));
}
