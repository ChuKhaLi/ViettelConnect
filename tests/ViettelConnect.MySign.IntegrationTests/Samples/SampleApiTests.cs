using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ViettelConnect.MySign.IntegrationTests.Fake;
using Xunit;

namespace ViettelConnect.MySign.IntegrationTests.Samples;

/// <summary>Sample API (chỉ để minh hoạ): ánh xạ lỗi MySign sang mã HTTP.</summary>
public sealed class SampleApiTests
{
    private static WebApplicationFactory<Program> Factory(string baseUrl) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Viettel:MySign:BaseUrl", baseUrl);
            b.UseSetting("Viettel:MySign:ClientId", "client-A");
            b.UseSetting("Viettel:MySign:ClientSecret", "secret-A");
            b.UseSetting("Viettel:MySign:ProfileId", "profile-1");
            b.UseSetting("Viettel:MySign:UserId", "user-1");
        });

    [Fact]
    public async Task Validation_error_maps_to_http_400()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        await using var factory = Factory(fake.BaseUrl);
        using var http = factory.CreateClient();

        // Base64 hợp lệ nhưng không phải 32 byte SHA-256 => MySignException loại Validation.
        var response = await http.PostAsJsonAsync("/viettel/sign", new { documentName = "Bien ban", sha256Base64 = "AQID" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Other_mysign_errors_map_to_http_502()
    {
        await using var fake = await FakeMySignServer.StartAsync();
        fake.Scenario.ErrorByRoute["ras/v1/login"] = (401, "{\"error\":\"59031\",\"error_description\":\"bad client\"}");
        await using var factory = Factory(fake.BaseUrl);
        using var http = factory.CreateClient();

        var response = await http.PostAsJsonAsync(
            "/viettel/sign",
            new { documentName = "Bien ban", sha256Base64 = Convert.ToBase64String(SHA256.HashData([1, 2, 3])) });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-A", body, StringComparison.Ordinal);
    }
}
