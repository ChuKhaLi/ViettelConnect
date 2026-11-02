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

public class SignSynchronousFakeServerTests
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

    private sealed class NoDelay : IDelayer
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
    }

    private static (SignHashes Sign, ListCertificates List) Build(FakeMySignServer fake)
    {
        // Chứng thư mặc định của fake đã hết hạn so với đồng hồ giả (2026-11-02).
        foreach (var cert in fake.Scenario.Certificates)
        {
            cert.ValidTo = "20301231000000Z";
        }

        var pipeline = TestPipeline.Create(fake.BaseUrl);
        var corr = new FixedCorrelation();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var sign = new SignHashes(
            pipeline.Wire,
            new FixedCredentials(),
            new FirstUsableCertificateSelector(time, corr),
            new MobileConfirmationAuthorizationProvider(),
            corr,
            new PollSignStatus(pipeline.Wire, new NoDelay(), time, corr),
            new MySignSigningSettings(SignMode.Synchronous, 2, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(5)));
        var list = new ListCertificates(pipeline.Wire, new FixedCredentials());
        return (sign, list);
    }

    private static HashDocument Doc(string id, string name = "Biên bản thí nghiệm") =>
        new(id, name, SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    [Fact]
    public async Task Three_documents_use_one_signHash_call_with_async_zero_and_ordered_signatures()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var (sign, _) = Build(fake);
        var docs = new[] { Doc("doc-1"), Doc("doc-2"), Doc("doc-3") };

        var result = await sign.ExecuteAsync(new SignHashesRequest(docs, "Ký biên bản", null), CancellationToken.None);

        var call = Assert.Single(fake.RequestsFor("signHash"));
        using var json = call.Json();
        Assert.Equal(0, json.RootElement.GetProperty("async").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("numSignatures").GetInt32());
        Assert.Equal("cred-001", result.Certificate.CredentialId);
        Assert.Equal(docs.Select(d => d.DocumentId), result.Signatures.Select(s => s.DocumentId));
        for (var i = 0; i < docs.Length; i++)
        {
            Assert.Equal(
                FakeMySignServer.SignatureFor(Convert.ToBase64String(docs[i].Sha256)),
                result.Signatures[i].Signature);
        }
    }

    [Fact]
    public async Task Hash_of_31_bytes_fails_validation_before_any_network_call()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var (sign, _) = Build(fake);
        var bad = new HashDocument("doc-1", "x", new byte[31]);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => sign.ExecuteAsync(new SignHashesRequest([bad], null, null), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Validation, ex.Category);
        Assert.Equal("corr-test", ex.CorrelationId);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Empty_documents_and_duplicate_ids_fail_validation_before_any_network_call()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var (sign, _) = Build(fake);

        var empty = await Assert.ThrowsAsync<MySignException>(
            () => sign.ExecuteAsync(new SignHashesRequest([], null, null), CancellationToken.None));
        var dup = await Assert.ThrowsAsync<MySignException>(
            () => sign.ExecuteAsync(new SignHashesRequest([Doc("a"), Doc("a")], null, null), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Validation, empty.Category);
        Assert.Equal(MySignErrorCategory.Validation, dup.Category);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Two_signatures_for_three_hashes_is_a_protocol_violation()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.DropLastSignature = true;
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => sign.ExecuteAsync(
                new SignHashesRequest([Doc("1"), Doc("2"), Doc("3")], null, null),
                CancellationToken.None));

        Assert.Equal(MySignErrorCategory.ProtocolViolation, ex.Category);
        Assert.Equal("corr-test", ex.CorrelationId);
    }

    [Fact]
    public async Task Unknown_credential_id_fails_with_no_active_certificate()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => sign.ExecuteAsync(new SignHashesRequest([Doc("1")], null, "does-not-exist"), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.NoActiveCertificate, ex.Category);
        Assert.Empty(fake.RequestsFor("signHash"));
    }

    [Fact]
    public async Task Explicit_known_credential_id_is_used()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.Certificates.Add(new FakeCertificate { CredentialId = "cred-002" });
        var (sign, _) = Build(fake);

        var result = await sign.ExecuteAsync(new SignHashesRequest([Doc("1")], null, "cred-002"), CancellationToken.None);

        Assert.Equal("cred-002", result.Certificate.CredentialId);
        using var json = Assert.Single(fake.RequestsFor("signHash")).Json();
        Assert.Equal("cred-002", json.RootElement.GetProperty("credentialID").GetString());
    }

    [Fact]
    public async Task List_certificates_returns_the_signers_certificates()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        var (_, list) = Build(fake);

        var certs = await list.ExecuteAsync(CancellationToken.None);

        Assert.Equal("cred-001", Assert.Single(certs).CredentialId);
    }

    [Fact]
    public async Task SignHash_503_is_not_retried_because_each_call_prompts_the_signer()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.FailOnceByRoute["signHash"] = 503;
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => sign.ExecuteAsync(new SignHashesRequest([Doc("1")], null, null), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Transport, ex.Category);
        Assert.Single(fake.RequestsFor("signHash"));
    }

    [Fact]
    public async Task Sync_signHash_58061_throws_timeout_exception_like_polling_mode()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["signHash"] = (400, "{\"error\":\"58061\",\"error_description\":\"Request timeout for mobile authorisation\"}");
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignTimeoutException>(
            () => sign.ExecuteAsync(new SignHashesRequest([Doc("1")], null, null), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.SignTimeout, ex.Category);
        Assert.Equal("58061", ex.RawCode);
        Assert.Equal("corr-test", ex.CorrelationId);
    }

    [Fact]
    public async Task Sync_signHash_58062_is_sign_rejected_not_timeout()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["signHash"] = (400, "{\"error\":\"58062\"}");
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => sign.ExecuteAsync(new SignHashesRequest([Doc("1")], null, null), CancellationToken.None));

        Assert.IsNotType<MySignTimeoutException>(ex);
        Assert.Equal(MySignErrorCategory.SignRejected, ex.Category);
        Assert.Equal("58062", ex.RawCode);
    }

    [Fact]
    public async Task SignHash_500_with_viettel_error_code_is_classified_by_the_code()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["signHash"] = (500, "{\"error\":\"58083\"}");
        var (sign, _) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(
            () => sign.ExecuteAsync(new SignHashesRequest([Doc("1")], null, null), CancellationToken.None));

        Assert.Equal(MySignErrorCategory.SignFailed, ex.Category);
        Assert.Equal("58083", ex.RawCode);
        Assert.Single(fake.RequestsFor("signHash"));
    }

    [Fact]
    public async Task Certificates_info_503_every_time_without_body_is_transport_after_retries()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["certificates/info"] = (503, string.Empty);
        var (_, list) = Build(fake);

        var ex = await Assert.ThrowsAsync<MySignException>(() => list.ExecuteAsync(CancellationToken.None));

        Assert.Equal(MySignErrorCategory.Transport, ex.Category);
        Assert.Equal(3, fake.RequestsFor("certificates/info").Count);
    }

    [Fact]
    public async Task Certificates_info_503_is_still_retried()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.FailOnceByRoute["certificates/info"] = 503;
        var (_, list) = Build(fake);

        var certs = await list.ExecuteAsync(CancellationToken.None);

        Assert.NotEmpty(certs);
        Assert.Equal(2, fake.RequestsFor("certificates/info").Count);
    }
}
