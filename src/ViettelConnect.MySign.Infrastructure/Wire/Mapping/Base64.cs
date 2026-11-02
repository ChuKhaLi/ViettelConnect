namespace ViettelConnect.MySign.Infrastructure.Wire.Mapping;

internal static class Base64
{
    /// <summary>Giải mã base64, bỏ qua khoảng trắng/xuống dòng nhúng giữa chuỗi (Viettel có thể ngắt dòng bằng CRLF).</summary>
    public static byte[] Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new FormatException("Empty base64 value.");
        }

        var buffer = new char[value.Length];
        var n = 0;
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
            {
                buffer[n++] = ch;
            }
        }

        return Convert.FromBase64CharArray(buffer, 0, n);
    }
}
