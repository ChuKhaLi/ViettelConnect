namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>Access token Bearer của MySign và hạn dùng (UTC).</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc)
{
    /// <summary>
    /// Thời điểm cấp (UTC) nếu biết. Dùng để nhận ra token có thời hạn rất ngắn và rút gọn biên hết hạn sớm;
    /// cache tự viết không lưu giá trị này thì để null (dùng biên mặc định).
    /// </summary>
    public DateTimeOffset? IssuedAtUtc { get; init; }

    /// <summary>Không bao giờ in giá trị token.</summary>
    public override string ToString() => $"AccessToken {{ Value = <redacted>, ExpiresAtUtc = {ExpiresAtUtc:O} }}";
}
