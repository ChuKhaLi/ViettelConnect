using System.Globalization;
using System.Text;

namespace ViettelConnect.MySign.Application.Signing;

/// <summary>
/// Mã hoá tên tài liệu hiển thị cho MySign: bỏ dấu tiếng Việt, chỉ giữ [A-Za-z0-9_- ],
/// rồi base64(UTF-8) với độ dài &lt; 100 ký tự.
/// </summary>
internal static class DisplayLabelEncoder
{
    // base64 length = 4 * ceil(n / 3); n = 72 gives 96 (< 100), n = 75 gives 100.
    private const int MaxSourceLength = 72;
    private const string LastResort = "Document";

    public static string Encode(string? label, string fallback)
    {
        var normalized = Normalize(label);
        if (normalized.Length == 0)
        {
            normalized = Normalize(fallback);
        }

        if (normalized.Length == 0)
        {
            normalized = LastResort;
        }

        if (normalized.Length > MaxSourceLength)
        {
            // Source is pure ASCII here, so cutting by char never splits a multi-byte sequence.
            normalized = normalized[..MaxSourceLength].TrimEnd();
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(normalized));
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var ch in StripLoneSurrogates(value).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var mapped = ch switch
            {
                'đ' => 'd',
                'Đ' => 'D',
                _ => ch,
            };

            if (mapped is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-')
            {
                if (pendingSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                pendingSpace = false;
                sb.Append(mapped);
            }
            else
            {
                pendingSpace = true;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// <see cref="string.Normalize(NormalizationForm)"/> ném <see cref="ArgumentException"/> với surrogate lẻ (vd JSON
    /// "\ud800"); thay chúng bằng khoảng trắng (coi như dấu phân cách) để không bao giờ ném lỗi.
    /// </summary>
    private static string StripLoneSurrogates(string value)
    {
        StringBuilder? sb = null;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            var paired = char.IsHighSurrogate(ch)
                ? i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])
                : !char.IsLowSurrogate(ch) || (i > 0 && char.IsHighSurrogate(value[i - 1]));

            if (paired)
            {
                sb?.Append(ch);
                continue;
            }

            sb ??= new StringBuilder(value, 0, i, value.Length);
            sb.Append(' ');
        }

        return sb?.ToString() ?? value;
    }
}
