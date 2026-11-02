using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Client;
using ViettelConnect.MySign.Client.DependencyInjection;
using ViettelConnect.MySign.Client.Dtos;
using ViettelConnect.MySign.Domain.Signing;
using ViettelConnect.MySign.Infrastructure.Configuration;
using ViettelConnect.MySign.IntegrationTests.Fake;
using Xunit;

namespace ViettelConnect.MySign.IntegrationTests.EndToEnd;

/// <summary>Toàn bộ đường đi qua <c>services.AddViettelMySign(...)</c> tới máy chủ MySign giả.</summary>
public class FacadeFakeServerTests
{
    private const string CallbackClientId = "viettel-cb";
    private const string CallbackSecret = "viettel-cb-secret";

    private sealed class SwitchableCredentials : IMySignCredentialsAccessor
    {
        private static readonly AsyncLocal<string?> CurrentUser = new();

        public static string? User
        {
            get => CurrentUser.Value;
            set => CurrentUser.Value = value;
        }

        public MySignCredentials Get() =>
            new("client-A", "secret-A", "profile-1", User ?? throw new InvalidOperationException("no signer"));
    }

    private static async Task<FakeMySignServer> StartFake()
    {
        var fake = await FakeMySignServer.StartAsync();
        foreach (var cert in fake.Scenario.Certificates)
        {
            cert.ValidTo = "20301231000000Z";
        }

        return fake;
    }

    private static ServiceProvider Build(FakeMySignServer fake, Action<MySignOptions>? extra = null, Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection();
        collection.AddViettelMySign(o =>
        {
            o.BaseUrl = fake.BaseUrl;
            o.ClientId = "client-A";
            o.ClientSecret = "secret-A";
            o.ProfileId = "profile-1";
            o.UserId = "user-1";
            o.Polling.Interval = TimeSpan.FromMilliseconds(10);
            o.Polling.TotalTimeout = TimeSpan.FromSeconds(10);
            o.Callback.ClientId = CallbackClientId;
            o.Callback.ClientSecret = CallbackSecret;
            extra?.Invoke(o);
        });
        services?.Invoke(collection);
        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static HashDocumentDto Doc(string id) =>
        new(id, "Biên bản thí nghiệm máy biến áp", SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    private static byte[] ExpectedSignature(HashDocumentDto doc) =>
        FakeMySignServer.SignatureFor(Convert.ToBase64String(doc.Sha256));

    private static string StatusCode(CallbackResponseDto r)
    {
        using var json = JsonDocument.Parse(r.JsonBody);
        return json.RootElement.GetProperty("status_code").GetString()!;
    }

    private static string UserIdOf(RecordedRequest login)
    {
        using var json = login.Json();
        return json.RootElement.GetProperty("user_id").GetString()!;
    }

    [Fact]
    public async Task ListCertificates_returns_public_dtos()
    {
        await using var fake = await StartFake();
        await using var sp = Build(fake);
        await using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IViettelMySignClient>();

        var certs = await client.ListCertificatesAsync();

        var cert = Assert.Single(certs);
        Assert.Equal("cred-001", cert.CredentialId);
        Assert.Equal("5AAC41CD8FA22B953640", cert.SerialNumber);
        Assert.Equal("CN=Nguyen Van A,O=Cong ty ABC", cert.SubjectDn);
        Assert.Equal("CN=Viettel-CA", cert.IssuerDn);
        Assert.True(cert.KeyEnabled);
        Assert.Equal(new DateTimeOffset(2030, 12, 31, 0, 0, 0, TimeSpan.Zero), cert.ValidTo);
        Assert.Equal(3, cert.Chain.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, cert.Chain[0]);
        Assert.Equal("user-1", UserIdOf(Assert.Single(fake.RequestsFor("ras/v1/login"))));
    }

    [Fact]
    public async Task SignHashes_defaults_to_polling_and_returns_raw_signatures_in_order()
    {
        await using var fake = await StartFake();
        fake.Scenario.StatusSequence["tx-1"] = ["4000", "1"];
        await using var sp = Build(fake);
        await using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IViettelMySignClient>();
        var docs = new[] { Doc("a"), Doc("b") };

        var result = await client.SignHashesAsync(new SignHashesRequestDto(docs, "Ký biên bản"));

        using (var sent = Assert.Single(fake.RequestsFor("signHash")).Json())
        {
            Assert.Equal(2, sent.RootElement.GetProperty("async").GetInt32());
        }

        Assert.Equal(2, fake.RequestsFor("requests/status").Count);
        Assert.Equal("cred-001", result.Certificate.CredentialId);
        Assert.Equal("tx-1", result.TransactionId);
        Assert.Equal(["a", "b"], result.Signatures.Select(s => s.DocumentId));
        Assert.Equal(ExpectedSignature(docs[0]), result.Signatures[0].Signature);
        Assert.Equal(ExpectedSignature(docs[1]), result.Signatures[1].Signature);
    }

    [Fact]
    public async Task SignHashes_in_synchronous_mode_sends_one_signHash()
    {
        await using var fake = await StartFake();
        await using var sp = Build(fake, o => o.SignMode = SignMode.Synchronous);
        await using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IViettelMySignClient>();
        var doc = Doc("a");

        var result = await client.SignHashesAsync(new SignHashesRequestDto([doc]));

        using (var sent = Assert.Single(fake.RequestsFor("signHash")).Json())
        {
            Assert.Equal(0, sent.RootElement.GetProperty("async").GetInt32());
        }

        Assert.Empty(fake.RequestsFor("requests/status"));
        Assert.Equal(ExpectedSignature(doc), Assert.Single(result.Signatures).Signature);
    }

    [Fact]
    public async Task BeginSignHashes_then_callback_handlers_complete_the_transaction()
    {
        await using var fake = await StartFake();
        await using var sp = Build(fake);
        var docs = new[] { Doc("a"), Doc("b") };

        BeginSignResultDto begun;
        await using (var scope = sp.CreateAsyncScope())
        {
            begun = await scope.ServiceProvider.GetRequiredService<IViettelMySignClient>()
                .BeginSignHashesAsync(new SignHashesRequestDto(docs));
        }

        using (var sent = Assert.Single(fake.RequestsFor("signHash")).Json())
        {
            Assert.Equal(3, sent.RootElement.GetProperty("async").GetInt32());
        }

        Assert.Equal("tx-1", begun.TransactionId);
        Assert.Equal("cred-001", begun.Certificate.CredentialId);

        // Viettel gọi về trong một request HTTP khác (scope khác) của ứng dụng.
        await using var callbackScope = sp.CreateAsyncScope();
        var client = callbackScope.ServiceProvider.GetRequiredService<IViettelMySignClient>();

        var tokenResponse = await client.HandleCallbackTokenRequestAsync(JsonSerializer.Serialize(new
        {
            grant_type = "client_credentials",
            client_id = CallbackClientId,
            client_secret = CallbackSecret,
        }));
        Assert.Equal(200, tokenResponse.HttpStatus);
        Assert.Equal("00", StatusCode(tokenResponse));
        string token;
        using (var json = JsonDocument.Parse(tokenResponse.JsonBody))
        {
            token = json.RootElement.GetProperty("data").GetProperty("access_token").GetString()!;
            Assert.Equal("3600000", json.RootElement.GetProperty("data").GetProperty("expires_in").GetString());
        }

        var resultBody = JsonSerializer.Serialize(new
        {
            meta_data = new { request_id = "VIETTEL_tx-1", transaction_id = begun.TransactionId, status = "1" },
            data = new
            {
                results = new[]
                {
                    new { document_id = "b", signature = Convert.ToBase64String(ExpectedSignature(docs[1])) },
                    new { document_id = "a", signature = Convert.ToBase64String(ExpectedSignature(docs[0])) },
                },
            },
        });

        var (response, outcome) = await client.HandleCallbackResultAsync(token, resultBody);

        Assert.Equal(200, response.HttpStatus);
        Assert.Equal("00", StatusCode(response));
        Assert.NotNull(outcome);
        Assert.Equal("default", outcome.TenantKey);
        Assert.Equal("tx-1", outcome.TransactionId);
        Assert.Equal(CallbackSignStatus.Succeeded, outcome.Status);
        Assert.Equal(["a", "b"], outcome.Signatures.Select(s => s.DocumentId));
        Assert.Equal(ExpectedSignature(docs[0]), outcome.Signatures[0].Signature);

        // Gửi lại sau khi đã hoàn tất: giao dịch không còn chờ, không bao giờ phát kết cục lần hai.
        var (_, duplicate) = await client.HandleCallbackResultAsync(token, resultBody);
        Assert.Null(duplicate);
    }

    [Fact]
    public async Task Callback_result_with_bad_token_is_rejected_without_outcome()
    {
        await using var fake = await StartFake();
        await using var sp = Build(fake);
        await using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IViettelMySignClient>();

        var (response, outcome) = await client.HandleCallbackResultAsync("not-a-token", "{}");

        Assert.Equal(200, response.HttpStatus);
        Assert.NotEqual("00", StatusCode(response));
        Assert.Null(outcome);

        var (missing, none) = await client.HandleCallbackResultAsync(null, "{}");
        Assert.NotEqual("00", StatusCode(missing));
        Assert.Null(none);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dynamic_mode_uses_app_accessor_per_call(bool registerBefore)
    {
        await using var fake = await StartFake();
        var collection = new ServiceCollection();
        if (registerBefore)
        {
            collection.AddSingleton<IMySignCredentialsAccessor, SwitchableCredentials>();
        }

        collection.AddViettelMySign(o =>
        {
            o.BaseUrl = fake.BaseUrl;
            o.CredentialsMode = CredentialsMode.Dynamic;
        });

        if (!registerBefore)
        {
            collection.AddSingleton<IMySignCredentialsAccessor, SwitchableCredentials>();
        }

        await using var sp = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IViettelMySignClient>();

        await RunAs("signer-1", () => client.ListCertificatesAsync());
        await RunAs("signer-2", () => client.ListCertificatesAsync());

        Assert.Equal(["signer-1", "signer-2"], fake.RequestsFor("ras/v1/login").Select(UserIdOf));
        Assert.Equal(2, fake.RequestsFor("certificates/info").Count);
    }

    private static async Task RunAs(string user, Func<Task> action)
    {
        // AsyncLocal đặt trong một luồng async con không rò ra ngoài lời gọi.
        await Task.Run(async () =>
        {
            SwitchableCredentials.User = user;
            await action();
        });
    }
}
