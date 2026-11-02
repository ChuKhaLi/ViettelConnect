using System.Text.RegularExpressions;

namespace ViettelConnect.MySign.Infrastructure.Logging;

/// <summary>
/// Che các chuỗi nhạy cảm trước khi chúng tới log hoặc thông điệp lỗi: <c>client_secret</c>, access/refresh
/// token, header <c>Authorization</c>/<c>Bearer</c>, <c>sad</c>, định danh người ký (<c>user_id</c>,
/// <c>profile_id</c>), mã băm và chữ ký, cùng mọi chuỗi base64 dài.
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

    // "name": "value"   hoặc   "name": ["v1","v2"]
    private static readonly Regex SensitiveJsonField = new(
        "\"(?<name>" + SensitiveNames + ")\"\\s*:\\s*(?:\"[^\"]*\"|\\[[^\\]]*\\])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Base64Chunk = new(
        @"[A-Za-z0-9+/]{32,}={0,2}",
        RegexOptions.Compiled);

    public static string Scrub(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = AuthHeaderPattern.Replace(input, m => $"Authorization: {Mask}");
        result = BearerPattern.Replace(result, $"Bearer {Mask}");
        result = SensitiveJsonField.Replace(result, m => $"\"{m.Groups["name"].Value}\":\"{Mask}\"");
        return Base64Chunk.Replace(result, Mask);
    }
}
