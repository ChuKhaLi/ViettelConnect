using System.Security.Cryptography;
using System.Text;

namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>
/// Bộ thông tin đăng nhập của một người ký: cặp client (theo đơn vị/lab) và định danh người ký
/// (<see cref="ProfileId"/>/<see cref="UserId"/>).
/// </summary>
public sealed record MySignCredentials(string ClientId, string ClientSecret, string ProfileId, string UserId)
{
    /// <summary>
    /// Khoá cache token: băm SHA-256 (hex thường, 64 ký tự) của <see cref="ClientId"/>, <see cref="ProfileId"/> và
    /// <see cref="UserId"/> (mỗi phần có tiền tố độ dài nên không nhập nhằng) — tách theo đơn vị và theo người ký, nhưng
    /// <see cref="ITokenCache"/> (có thể là cache phân tán) không bao giờ thấy định danh người ký ở dạng thường.
    /// <see cref="ClientSecret"/> cố ý không nằm trong khoá: credentials đến từ accessor tin cậy của ứng dụng (không phải
    /// đầu vào của người dùng cuối), nên token đã cache của một người ký không cần phân biệt theo secret.
    /// </summary>
    public string CacheKey
    {
        get
        {
            var material = $"{ClientId.Length}:{ClientId}|{ProfileId.Length}:{ProfileId}|{UserId.Length}:{UserId}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Che <see cref="ClientSecret"/> và định danh người ký (<see cref="ProfileId"/>, <see cref="UserId"/>);
    /// <c>ToString()</c> sinh sẵn của record sẽ in tất cả ở dạng thường.
    /// </summary>
    public override string ToString() =>
        $"MySignCredentials {{ ClientId = {ClientId}, ClientSecret = <redacted>, ProfileId = <redacted>, UserId = <redacted> }}";
}
