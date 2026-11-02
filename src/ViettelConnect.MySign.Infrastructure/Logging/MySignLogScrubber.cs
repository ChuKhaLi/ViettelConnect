using System.Text.RegularExpressions;

namespace ViettelConnect.MySign.Infrastructure.Logging;

/// <summary>
/// Che các chuỗi nhạy cảm trước khi chúng tới log hoặc thông điệp lỗi: <c>client_secret</c>, access/refresh
/// token, header <c>Authorization</c>/<c>Bearer</c>, <c>sad</c>, định danh người ký (<c>user_id</c>,
/// <c>profile_id</c>), mã băm và chữ ký (giá trị chuỗi có escape, mảng, hoặc số/true/false/null), cùng mọi chuỗi
/// base64 dài từ 32 ký tự — trừ chuỗi chỉ gồm hex và ngắn hơn 40 ký tự (GUID "N", mã 32 hex) để log còn đối soát được.
/// </summary>
internal static class MySignLogScrubber
{
    internal const string Mask = "<redacted>";

    private static readonly Regex BearerPattern = new(
        @"Bearer\s+[A-Za-z0-9\-_=\.\+/]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AuthHeaderPattern = new(
        @"\bAuthorization\s*[:=]\s*[^\r\n,;""]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string SensitiveNames =
        "client_secret|access_token|refresh_token|token|authorization|sad|user_id|userId|profile_id|profileId|" +
        "signature|signatures|hash|hashes|certificates";

    // Chuỗi JSON (có escape \" và \\), mảng (phần tử chuỗi có thể chứa "]"), hoặc giá trị vô hướng không có ngoặc kép.
    private const string JsonString = @"""(?:[^""\\]|\\.)*""";
    private const string JsonArray = @"\[(?:" + JsonString + @"|[^\]""])*\]";
    private const string JsonScalar = @"-?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?|true|false|null";

    // "name": "value"   hoặc   "name": ["v1","v2"]   hoặc   "name": 123 / true / false / null
    private static readonly Regex SensitiveJsonField = new(
        @"""(?<name>" + SensitiveNames + @")""\s*:\s*(?:" + JsonString + "|" + JsonArray + "|(?:" + JsonScalar + @")(?![A-Za-z0-9_]))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Base64Chunk = new(
        @"[A-Za-z0-9+/]{32,}={0,2}",
        RegexOptions.Compiled);

    /// <summary>
    /// Chuỗi chỉ gồm chữ số hex và ngắn hơn ngưỡng này được giữ nguyên (GUID dạng "N", mã giao dịch/mã tương quan 32 hex).
    /// Từ ngưỡng trở lên (vd SHA-1 40 hex, SHA-256 64 hex) vẫn bị che như base64.
    /// </summary>
    private const int HexIdentifierMaxLength = 39;

    public static string Scrub(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = AuthHeaderPattern.Replace(input, m => $"Authorization: {Mask}");
        result = BearerPattern.Replace(result, $"Bearer {Mask}");
        result = SensitiveJsonField.Replace(result, m => $"\"{m.Groups["name"].Value}\":\"{Mask}\"");
        return Base64Chunk.Replace(result, m => IsShortHexIdentifier(m.Value) ? m.Value : Mask);
    }

    private static bool IsShortHexIdentifier(string value)
    {
        if (value.Length > HexIdentifierMaxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
