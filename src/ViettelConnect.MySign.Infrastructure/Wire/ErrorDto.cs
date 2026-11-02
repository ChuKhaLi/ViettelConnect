using System.Text.Json.Serialization;

namespace ViettelConnect.MySign.Infrastructure.Wire;

/// <summary>Thân lỗi <c>{error, error_description}</c>; mục tham số của tài liệu còn ghi tên <c>error_code</c>.</summary>
internal sealed class ErrorDto
{
    [JsonPropertyName("error")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? Error { get; set; }

    [JsonPropertyName("error_code")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("error_description")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? ErrorDescription { get; set; }
}
