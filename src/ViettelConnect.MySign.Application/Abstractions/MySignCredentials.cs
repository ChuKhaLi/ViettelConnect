namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>
/// Bộ thông tin đăng nhập của một người ký: cặp client (theo đơn vị/lab) và định danh người ký
/// (<see cref="ProfileId"/>/<see cref="UserId"/>).
/// </summary>
public sealed record MySignCredentials(string ClientId, string ClientSecret, string ProfileId, string UserId)
{
    /// <summary>Khoá cache token: tách theo đơn vị và theo người ký. Chỉ dùng làm khoá từ điển, không ghi log.</summary>
    public string CacheKey => $"{ClientId}|{ProfileId}|{UserId}";

    /// <summary>
    /// Che <see cref="ClientSecret"/> và định danh người ký (<see cref="ProfileId"/>, <see cref="UserId"/>);
    /// <c>ToString()</c> sinh sẵn của record sẽ in tất cả ở dạng thường.
    /// </summary>
    public override string ToString() =>
        $"MySignCredentials {{ ClientId = {ClientId}, ClientSecret = <redacted>, ProfileId = <redacted>, UserId = <redacted> }}";
}
