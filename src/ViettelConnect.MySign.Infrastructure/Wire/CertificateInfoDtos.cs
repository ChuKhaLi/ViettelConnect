using System.Text.Json.Serialization;

namespace ViettelConnect.MySign.Infrastructure.Wire;

internal sealed class CertificateInfoRequestDto
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("client_secret")]
    public string ClientSecret { get; set; } = string.Empty;

    [JsonPropertyName("profile_id")]
    public string ProfileId { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("certificates")]
    public string Certificates { get; set; } = "chain";

    [JsonPropertyName("certInfo")]
    public bool CertInfo { get; set; } = true;

    [JsonPropertyName("authInfo")]
    public bool AuthInfo { get; set; } = true;
}

internal sealed class CertificateInfoDto
{
    [JsonPropertyName("credential_id")]
    public string? CredentialId { get; set; }

    [JsonPropertyName("key")]
    public CertificateKeyDto? Key { get; set; }

    [JsonPropertyName("cert")]
    public CertificateBodyDto? Cert { get; set; }
}

internal sealed class CertificateKeyDto
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("algo")]
    public List<string>? Algo { get; set; }
}

internal sealed class CertificateBodyDto
{
    [JsonPropertyName("certificates")]
    public List<string>? Certificates { get; set; }

    [JsonPropertyName("SerialNumber")]
    public string? SerialNumber { get; set; }

    [JsonPropertyName("subjectDN")]
    public string? SubjectDn { get; set; }

    [JsonPropertyName("issuerDN")]
    public string? IssuerDn { get; set; }

    [JsonPropertyName("validFrom")]
    public string? ValidFrom { get; set; }

    [JsonPropertyName("validTo")]
    public string? ValidTo { get; set; }
}
