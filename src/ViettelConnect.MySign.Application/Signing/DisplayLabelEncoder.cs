using System.Globalization;
using System.Text;

namespace ViettelConnect.MySign.Application.Signing;

/// <summary>
/// Mã hoá tên tài liệu hiển thị cho MySign: bỏ dấu tiếng Việt, chỉ giữ [A-Za-z0-9_- ],
/// rồi base64(UTF-8) với độ dài &lt; 100 ký tự.
/// </summary>
public static class DisplayLabelEncoder
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

        foreach (var ch in value.Normalize(NormalizationForm.FormD))
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
}
