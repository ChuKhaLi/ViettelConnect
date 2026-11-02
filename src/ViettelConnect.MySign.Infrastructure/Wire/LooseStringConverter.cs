using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViettelConnect.MySign.Infrastructure.Wire;

/// <summary>
/// Đọc giá trị thành chuỗi dù Viettel gửi dạng chuỗi, số hay boolean
/// (vd <c>expires_in</c> được tài liệu ghi là chuỗi giây nhưng có thể về dạng số).
/// </summary>
internal sealed class LooseStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var l) => l.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number when reader.TryGetDouble(out var d) => d.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => null,
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
