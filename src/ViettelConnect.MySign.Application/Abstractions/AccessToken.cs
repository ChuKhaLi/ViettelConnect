namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>Access token Bearer của MySign và hạn dùng (UTC).</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc)
{
    /// <summary>Không bao giờ in giá trị token.</summary>
    public override string ToString() => $"AccessToken {{ Value = <redacted>, ExpiresAtUtc = {ExpiresAtUtc:O} }}";
}
