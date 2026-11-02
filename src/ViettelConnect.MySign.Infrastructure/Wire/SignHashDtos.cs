using System.Text.Json.Serialization;

namespace ViettelConnect.MySign.Infrastructure.Wire;

internal sealed class SignHashRequestDto
{
    [JsonPropertyName("credentialID")]
    public string CredentialId { get; set; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("client_secret")]
    public string ClientSecret { get; set; } = string.Empty;

    [JsonPropertyName("numSignatures")]
    public int NumSignatures { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("documents")]
    public List<SignHashDocumentDto> Documents { get; set; } = new();

    [JsonPropertyName("hash")]
    public List<string> Hash { get; set; } = new();

    [JsonPropertyName("hashAlgo")]
    public string HashAlgo { get; set; } = string.Empty;

    [JsonPropertyName("signAlgo")]
    public string SignAlgo { get; set; } = string.Empty;

    [JsonPropertyName("async")]
    public int Async { get; set; }

    /// <summary>Chỉ ghi khi khác null (SCAL2).</summary>
    [JsonPropertyName("SAD")]
    public string? Sad { get; set; }
}

internal sealed class SignHashDocumentDto
{
    /// <summary>Số thứ tự 1..n theo thứ tự gửi (JSON number, như code demo của Viettel); không phải DocumentId của ứng dụng.</summary>
    [JsonPropertyName("document_id")]
    public int DocumentId { get; set; }

    [JsonPropertyName("document_name")]
    public string DocumentName { get; set; } = string.Empty;
}

internal sealed class SignHashResponseDto
{
    [JsonPropertyName("signatures")]
    public List<string>? Signatures { get; set; }

    [JsonPropertyName("transactionId")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? TransactionId { get; set; }
}
