using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Domain.Signing;
using ViettelConnect.MySign.Infrastructure.Configuration;
using ViettelConnect.MySign.Infrastructure.Wire;
using ViettelConnect.MySign.IntegrationTests.Fake;
using Xunit;

namespace ViettelConnect.MySign.IntegrationTests.Wire;

public class MySignWireClientTests
{
    private const string Secret = "S3cr3t-value-XYZ";
    private static readonly MySignCredentials Creds = new("client-1", Secret, "profile-1", "user-1");

    private sealed class FixedCorrelation : ICorrelationIdAccessor
    {
        public string Current => "corr-test";
    }

    private sealed class RecordingHandler : DelegatingHandler
    {
        public List<(string Path, MySignCredentials? Credentials, bool SkipAuth)> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Options.TryGetValue(MySignRequestOptions.Credentials, out var creds);
            request.Options.TryGetValue(MySignRequestOptions.SkipAuth, out var skip);
            Seen.Add((request.RequestUri!.AbsolutePath, creds, skip));
            return base.SendAsync(request, cancellationToken);
        }
    }

    private static MySignWireClient NewClient(
        string baseUrl,
        FakeTimeProvider? time = null,
        bool includeRaw = false,
        HttpMessageHandler? handler = null)
    {
        var options = Options.Create(new MySignOptions
        {
            BaseUrl = baseUrl,
            Errors = new ErrorOptions { IncludeRawErrorMessage = includeRaw },
        });
        var http = handler is null ? new HttpClient() : new HttpClient(handler);
        return new MySignWireClient(http, options, new FixedCorrelation(), time ?? new FakeTimeProvider());
    }

    private static SignHashWireRequest SignRequest(int asyncCode, string? sad = null, int count = 2)
    {
        var docs = Enumerable.Range(1, count)
            .Select(i => ($"doc-{i}", $"RG9j{i}", new byte[] { (byte)i, 0xAA, 0xBB }))
            .ToArray();
        return new SignHashWireRequest("cred-001", docs, "RGVzYw==", asyncCode, sad);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public async Task Login_sends_exactly_four_fields_and_reads_expires_in_string(string suffix)
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var client = NewClient(fake.BaseUrl + suffix, time);

        var token = await client.LoginAsync(Creds, CancellationToken.None);

        Assert.Equal("fake-token-1", token.Value);
        Assert.Equal(time.GetUtcNow().AddSeconds(3600), token.ExpiresAtUtc);

        var req = Assert.Single(fake.RequestsFor("ras/v1/login"));
        Assert.Null(req.Authorization);
        using var doc = req.Json();
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["client_id", "client_secret", "profile_id", "user_id"], names);
        Assert.Equal("client-1", doc.RootElement.GetProperty("client_id").GetString());
        Assert.Equal(Secret, doc.RootElement.GetProperty("client_secret").GetString());
        Assert.Equal("profile-1", doc.RootElement.GetProperty("profile_id").GetString());
        Assert.Equal("user-1", doc.RootElement.GetProperty("user_id").GetString());
    }

    [Fact]
    public async Task Login_accepts_numeric_expires_in()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["ras/v1/login"] = (200, "{\"access_token\":\"num-token\",\"token_type\":\"Bearer\",\"expires_in\":1800}");
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));

        var token = await NewClient(fake.BaseUrl, time).LoginAsync(Creds, CancellationToken.None);

        Assert.Equal("num-token", token.Value);
        Assert.Equal(time.GetUtcNow().AddSeconds(1800), token.ExpiresAtUtc);
    }

    [Fact]
    public async Task Error_body_with_only_error_code_is_still_mapped()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["ras/v1/login"] = (401, "{\"error_code\":\"59033\"}");

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => NewClient(fake.BaseUrl).LoginAsync(Creds, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Authentication, ex.Category);
        Assert.Equal("59033", ex.RawCode);
    }

    [Fact]
    public async Task Login_missing_access_token_is_protocol_violation()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["ras/v1/login"] = (200, "{\"token_type\":\"Bearer\",\"expires_in\":\"3600\"}");

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => NewClient(fake.BaseUrl).LoginAsync(Creds, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.ProtocolViolation, ex.Category);
        Assert.Equal("corr-test", ex.CorrelationId);
    }

    [Fact]
    public async Task Requests_carry_credentials_in_options_and_only_login_skips_auth()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var handler = new RecordingHandler { InnerHandler = new HttpClientHandler() };
        var client = NewClient(fake.BaseUrl, handler: handler);

        await client.LoginAsync(Creds, CancellationToken.None);
        await client.GetCertificatesAsync(Creds, CancellationToken.None);
        await client.SignHashAsync(Creds, SignRequest(0), CancellationToken.None);
        await client.GetStatusAsync(Creds, "tx-x", CancellationToken.None);

        Assert.Equal(4, handler.Seen.Count);
        Assert.All(handler.Seen, s => Assert.Equal(Creds, s.Credentials));
        Assert.True(handler.Seen[0].SkipAuth);
        Assert.All(handler.Seen.Skip(1), s => Assert.False(s.SkipAuth));
        // Task 5 chưa có auth handler: các lời gọi không phải login đi không kèm Bearer.
        Assert.All(fake.Requests.Skip(1), r => Assert.Null(r.Authorization));
    }

    [Fact]
    public async Task GetCertificates_maps_all_fields_and_keeps_der_order()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var client = NewClient(fake.BaseUrl);

        var certs = await client.GetCertificatesAsync(Creds, CancellationToken.None);

        var req = Assert.Single(fake.RequestsFor("certificates/info"));
        using (var doc = req.Json())
        {
            Assert.Equal("chain", doc.RootElement.GetProperty("certificates").GetString());
            Assert.True(doc.RootElement.GetProperty("certInfo").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("authInfo").GetBoolean());
            Assert.Equal("client-1", doc.RootElement.GetProperty("client_id").GetString());
            Assert.Equal(Secret, doc.RootElement.GetProperty("client_secret").GetString());
            Assert.Equal("profile-1", doc.RootElement.GetProperty("profile_id").GetString());
            Assert.Equal("user-1", doc.RootElement.GetProperty("user_id").GetString());
        }

        var cert = Assert.Single(certs);
        Assert.Equal("cred-001", cert.CredentialId);
        Assert.Equal("5AAC41CD8FA22B953640", cert.SerialNumber);
        Assert.Equal("CN=Nguyen Van A,O=Cong ty ABC", cert.SubjectDn);
        Assert.Equal("CN=Viettel-CA", cert.IssuerDn);
        Assert.Equal(new DateTimeOffset(2024, 7, 9, 13, 22, 16, TimeSpan.Zero), cert.ValidFrom);
        Assert.Equal(new DateTimeOffset(2030, 12, 31, 0, 0, 0, TimeSpan.Zero), cert.ValidTo);
        Assert.True(cert.KeyEnabled);
        Assert.Equal(["1.2.840.113549.1.1.1"], cert.SignAlgorithmOids);
        Assert.Equal(3, cert.Chain.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, cert.Chain[0]);
        Assert.Equal(new byte[] { 4, 5 }, cert.Chain[1]);
        Assert.Equal(new byte[] { 6 }, cert.Chain[2]);
    }

    [Fact]
    public async Task GetCertificates_disabled_key_is_not_enabled()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.Certificates[0].KeyStatus = "DISABLED";

        var certs = await NewClient(fake.BaseUrl).GetCertificatesAsync(Creds, CancellationToken.None);

        Assert.False(Assert.Single(certs).KeyEnabled);
    }

    [Fact]
    public async Task GetCertificates_non_array_body_is_protocol_violation()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["certificates/info"] = (200, "{\"credential_id\":\"x\"}");

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => NewClient(fake.BaseUrl).GetCertificatesAsync(Creds, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.ProtocolViolation, ex.Category);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SignHash_sends_counts_oids_async_and_omits_sad_when_null(int asyncCode)
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var client = NewClient(fake.BaseUrl);

        var response = await client.SignHashAsync(Creds, SignRequest(asyncCode), CancellationToken.None);

        var req = Assert.Single(fake.RequestsFor("signHash"));
        using var doc = req.Json();
        var root = doc.RootElement;
        Assert.Equal("cred-001", root.GetProperty("credentialID").GetString());
        Assert.Equal("client-1", root.GetProperty("client_id").GetString());
        Assert.Equal(Secret, root.GetProperty("client_secret").GetString());
        var hashes = root.GetProperty("hash").EnumerateArray().Select(e => e.GetString()!).ToArray();
        var docs = root.GetProperty("documents").EnumerateArray().ToArray();
        Assert.Equal(2, root.GetProperty("numSignatures").GetInt32());
        Assert.Equal(2, hashes.Length);
        Assert.Equal(2, docs.Length);
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 0xAA, 0xBB }), hashes[0]);
        Assert.Equal("doc-1", docs[0].GetProperty("document_id").GetString());
        Assert.Equal("RG9j1", docs[0].GetProperty("document_name").GetString());
        Assert.Equal("RGVzYw==", root.GetProperty("description").GetString());
        Assert.Equal("2.16.840.1.101.3.4.2.1", root.GetProperty("hashAlgo").GetString());
        Assert.Equal("1.2.840.113549.1.1.1", root.GetProperty("signAlgo").GetString());
        Assert.Equal(asyncCode, root.GetProperty("async").GetInt32());
        Assert.False(root.TryGetProperty("SAD", out _));

        if (asyncCode == 0)
        {
            Assert.Null(response.TransactionId);
            Assert.NotNull(response.Signatures);
            Assert.Equal(2, response.Signatures!.Count);
            Assert.Equal(FakeMySignServer.SignatureFor(hashes[0]), response.Signatures[0]);
            Assert.Equal(FakeMySignServer.SignatureFor(hashes[1]), response.Signatures[1]);
        }
        else
        {
            Assert.Equal("tx-1", response.TransactionId);
            Assert.Null(response.Signatures);
        }
    }

    [Fact]
    public async Task SignHash_sends_sad_when_present_and_omits_description_when_null()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var request = SignRequest(0, sad: "sad-value") with { EncodedDescription = null };

        await NewClient(fake.BaseUrl).SignHashAsync(Creds, request, CancellationToken.None);

        using var doc = Assert.Single(fake.RequestsFor("signHash")).Json();
        Assert.Equal("sad-value", doc.RootElement.GetProperty("SAD").GetString());
        Assert.False(doc.RootElement.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task SignHash_decodes_signatures_with_embedded_crlf()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.WrapSignaturesWithCrLf = true;
        var request = SignRequest(0, count: 1);

        var response = await NewClient(fake.BaseUrl).SignHashAsync(Creds, request, CancellationToken.None);

        var hash = Convert.ToBase64String(request.Documents[0].Sha256);
        Assert.Equal(FakeMySignServer.SignatureFor(hash), Assert.Single(response.Signatures!));
    }

    [Fact]
    public async Task SignHash_with_neither_signatures_nor_transaction_is_protocol_violation()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["signHash"] = (200, "{}");

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => NewClient(fake.BaseUrl).SignHashAsync(Creds, SignRequest(2), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.ProtocolViolation, ex.Category);
    }

    [Fact]
    public async Task GetStatus_follows_sequence_and_returns_signatures_on_success()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var client = NewClient(fake.BaseUrl);
        var sign = await client.SignHashAsync(Creds, SignRequest(2, count: 1), CancellationToken.None);
        var tx = sign.TransactionId!;
        fake.Scenario.StatusSequence[tx] = ["4000", "6000", "1"];

        var first = await client.GetStatusAsync(Creds, tx, CancellationToken.None);
        var second = await client.GetStatusAsync(Creds, tx, CancellationToken.None);
        var third = await client.GetStatusAsync(Creds, tx, CancellationToken.None);

        Assert.Equal("4000", first.RawStatus);
        Assert.Equal(SignStatus.Pending, first.Status);
        Assert.Empty(first.Signatures);
        Assert.Equal(SignStatus.Pending, second.Status);
        Assert.Equal("1", third.RawStatus);
        Assert.Equal(SignStatus.Succeeded, third.Status);
        Assert.Single(third.Signatures);

        var bodies = fake.RequestsFor("requests/status").Select(r => r.Json().RootElement.GetProperty("transactionId").GetString());
        Assert.All(bodies, b => Assert.Equal(tx, b));
    }

    [Theory]
    [InlineData("4002", nameof(SignStatus.Rejected))]
    [InlineData("4001", nameof(SignStatus.TimedOut))]
    [InlineData("4004", nameof(SignStatus.Failed))]
    [InlineData("4999", nameof(SignStatus.Pending))]
    public async Task GetStatus_classifies_terminal_and_unknown_codes(string raw, string expected)
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.StatusSequence["tx-q"] = [raw];

        var snap = await NewClient(fake.BaseUrl).GetStatusAsync(Creds, "tx-q", CancellationToken.None);

        Assert.Equal(raw, snap.RawStatus);
        Assert.Equal(Enum.Parse<SignStatus>(expected), snap.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Error_401_59033_maps_to_authentication_and_raw_body_is_gated(bool includeRaw)
    {
        await using var fake = await FakeMySignServer.StartAsync();
        const string marker = "RAW-MARKER-user-id-or-password-invalid";
        fake.Scenario.ErrorByRoute["ras/v1/login"] =
            (401, "{\"error\":\"59033\",\"error_description\":\"" + marker + " " + Secret + "\"}");

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => NewClient(fake.BaseUrl, includeRaw: includeRaw).LoginAsync(Creds, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Authentication, ex.Category);
        Assert.Equal("59033", ex.RawCode);
        Assert.Equal("corr-test", ex.CorrelationId);
        Assert.DoesNotContain(Secret, ex.Detail);
        Assert.DoesNotContain(Secret, ex.Message);
        if (includeRaw)
        {
            Assert.Contains(marker, ex.Detail);
        }
        else
        {
            Assert.DoesNotContain(marker, ex.Detail);
        }
    }

    [Fact]
    public async Task Error_400_invalid_request_maps_to_validation()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["signHash"] = (400, "{\"error\":\"invalid_request\",\"error_description\":\"Invalid parameter credentialID\"}");

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => NewClient(fake.BaseUrl).SignHashAsync(Creds, SignRequest(0), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Validation, ex.Category);
        Assert.Equal("invalid_request", ex.RawCode);
        Assert.DoesNotContain("Invalid parameter", ex.Detail);
    }

    [Fact]
    public async Task Error_5xx_non_json_body_is_transport_without_leaking_body()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["requests/status"] = (502, "<html>Bad gateway SECRET-PAGE</html>");

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => NewClient(fake.BaseUrl).GetStatusAsync(Creds, "tx-1", CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Transport, ex.Category);
        Assert.Null(ex.RawCode);
        Assert.DoesNotContain("SECRET-PAGE", ex.Detail);
    }

    [Fact]
    public async Task Success_with_malformed_json_is_protocol_violation()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["requests/status"] = (200, "not json at all");

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => NewClient(fake.BaseUrl).GetStatusAsync(Creds, "tx-1", CancellationToken.None));

        Assert.Equal(MySignErrorCategory.ProtocolViolation, ex.Category);
    }

    [Fact]
    public async Task Connection_failure_is_transport_error()
    {
        string deadUrl;
        await using (var fake = await FakeMySignServer.StartAsync())
        {
            deadUrl = fake.BaseUrl;
        }

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => NewClient(deadUrl).LoginAsync(Creds, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Transport, ex.Category);
        Assert.DoesNotContain(Secret, ex.Detail);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_wrapped()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NewClient(fake.BaseUrl).LoginAsync(Creds, cts.Token));
    }

    [Fact]
    public async Task Bearer_toggle_rejects_unauthenticated_calls_but_not_login()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.RequireBearer = true;
        var client = NewClient(fake.BaseUrl);

        await client.LoginAsync(Creds, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<MySignException>(
            () => client.GetCertificatesAsync(Creds, CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Authentication, ex.Category);
        Assert.Equal("59033", ex.RawCode);
        Assert.Equal(1, fake.LoginCount);
    }
}
