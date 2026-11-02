using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ViettelConnect.MySign.Client;
using ViettelConnect.MySign.Client.DependencyInjection;
using ViettelConnect.MySign.Client.Dtos;
using ViettelConnect.MySign.Domain.Signing;
using Xunit;

namespace ViettelConnect.MySign.IntegrationTests.RealAccount;

/// <summary>
/// Kiểm tra khói với tài khoản MySign thật (người ký phải bấm xác nhận trên ứng dụng MySign). Chỉ chạy khi có đủ biến môi trường
/// <c>VIETTEL_MYSIGN_*</c>; tuỳ chọn <c>VIETTEL_MYSIGN_BASE_URL</c>. Chưa từng chạy với tài khoản thật.
/// </summary>
public class RealAccountSmokeTests
{
    [RealAccountFact]
    public async Task List_certificates_and_sign_one_hash_verifies_against_chain_head()
    {
        var baseUrl = Environment.GetEnvironmentVariable("VIETTEL_MYSIGN_BASE_URL");

        var services = new ServiceCollection();
        services.AddViettelMySign(o =>
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                o.BaseUrl = baseUrl;
            }

            o.ClientId = Environment.GetEnvironmentVariable(RealAccountFactAttribute.ClientIdVar)!;
            o.ClientSecret = Environment.GetEnvironmentVariable(RealAccountFactAttribute.ClientSecretVar)!;
            o.ProfileId = Environment.GetEnvironmentVariable(RealAccountFactAttribute.ProfileIdVar)!;
            o.UserId = Environment.GetEnvironmentVariable(RealAccountFactAttribute.UserIdVar)!;
            o.SignMode = SignMode.Polling;
        });

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IViettelMySignClient>();

        var certificates = await client.ListCertificatesAsync();
        Assert.NotEmpty(certificates);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"viettelconnect-smoke-{Guid.NewGuid():N}"));
        var result = await client.SignHashesAsync(new SignHashesRequestDto(
            [new HashDocumentDto("smoke-1", "ViettelConnect smoke test", hash)],
            Description: "ViettelConnect smoke test"));

        var signature = Assert.Single(result.Signatures);
        Assert.NotEmpty(result.Certificate.Chain);

        // signAlgo của SDK là rsaEncryption (PKCS#1 v1.5) trên SHA-256; chứng thư người ký đứng đầu chuỗi.
        using var signer = new X509Certificate2(result.Certificate.Chain[0]);
        using var rsa = signer.GetRSAPublicKey();
        Assert.NotNull(rsa);
        Assert.True(rsa.VerifyHash(hash, signature.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
}
