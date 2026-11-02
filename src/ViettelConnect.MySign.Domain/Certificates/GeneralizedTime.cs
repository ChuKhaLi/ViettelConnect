using System.Globalization;

namespace ViettelConnect.MySign.Domain.Certificates;

/// <summary>Phân tích thời gian dạng GeneralizedTime: YYYYMMDDHHMMSS cộng Z hoặc +hhmm/-hhmm.</summary>
public static class GeneralizedTime
{
    private const int DateTimeLength = 14;

    public static DateTimeOffset? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length <= DateTimeLength)
        {
            return null;
        }

        var offset = TimeSpan.Zero;
        var zone = text[DateTimeLength..];
        if (zone != "Z")
        {
            if (zone.Length != 5 || (zone[0] != '+' && zone[0] != '-')
                || !int.TryParse(zone.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hh)
                || !int.TryParse(zone.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var mm)
                || hh > 14 || mm > 59)
            {
                return null;
            }

            offset = new TimeSpan(hh, mm, 0);
            if (zone[0] == '-')
            {
                offset = -offset;
            }
        }

        if (!DateTime.TryParseExact(
                text[..DateTimeLength],
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local))
        {
            return null;
        }

        return new DateTimeOffset(local, offset);
    }
}
