using System.Text.Json.Serialization;

namespace ViettelConnect.MySign.Infrastructure.Wire;

internal sealed class RequestStatusRequestDto
{
    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;
}

internal sealed class RequestStatusResponseDto
{
    [JsonPropertyName("signatures")]
    public List<string>? Signatures { get; set; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? Status { get; set; }

    [JsonPropertyName("error_code")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("error_description")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? ErrorDescription { get; set; }
}
