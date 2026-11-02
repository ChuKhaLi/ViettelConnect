using ViettelConnect.MySign.Client.Dtos;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Client;

/// <summary>DTO chứa chữ ký hoặc token không được in nội dung đó qua <c>ToString</c>.</summary>
public class DtoRedactionTests
{
    private static readonly byte[] Signature = [0xDE, 0xAD, 0xBE, 0xEF];

    private static CertificateDto Cert() =>
        new("cred-001", "SN", "CN=Signer", "CN=Viettel-CA", null, null, true, [[1, 2, 3]]);

    [Fact]
    public void DocumentSignature_hides_signature()
    {
        var text = new DocumentSignatureDto("doc-1", Signature).ToString();
        Assert.Contains("doc-1", text);
        Assert.Contains("<redacted>", text);
        Assert.DoesNotContain(Convert.ToBase64String(Signature), text);
    }

    [Fact]
    public void CallbackResponse_hides_json_body()
    {
        var text = new CallbackResponseDto(200, "{\"data\":{\"access_token\":\"secret-token\"}}").ToString();
        Assert.Contains("200", text);
        Assert.DoesNotContain("secret-token", text);
    }

    [Fact]
    public void SignHashesResult_and_outcome_show_counts_only()
    {
        var signatures = new[] { new DocumentSignatureDto("doc-1", Signature) };
        var result = new SignHashesResultDto(Cert(), signatures, "tx-1").ToString();
        Assert.Contains("Signatures = 1", result);
        Assert.DoesNotContain("DEADBEEF", result, StringComparison.OrdinalIgnoreCase);

        var outcome = new CallbackSignOutcomeDto("lab", "tx-1", CallbackSignStatus.Succeeded, signatures, "1").ToString();
        Assert.Contains("Succeeded", outcome);
        Assert.Contains("Signatures = 1", outcome);
    }
}
