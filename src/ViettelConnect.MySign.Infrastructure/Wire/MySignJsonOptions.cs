using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViettelConnect.MySign.Infrastructure.Wire;

internal static class MySignJsonOptions
{
    /// <summary>
    /// Ghi đúng tên trường theo <see cref="JsonPropertyNameAttribute"/> (tài liệu Viettel viết hoa/thường
    /// không nhất quán, vd <c>SerialNumber</c> cạnh <c>subjectDN</c>), đọc không phân biệt hoa/thường.
    /// </summary>
    public static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
