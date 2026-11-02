using ViettelConnect.MySign.Application.Abstractions;

namespace ViettelConnect.MySign.Infrastructure.Wire;

/// <summary>
/// Khoá <see cref="HttpRequestMessage.Options"/> để handler xác thực (Task 6) biết lời gọi thuộc về ai
/// và có cần Bearer hay không (login thì không).
/// </summary>
internal static class MySignRequestOptions
{
    public static readonly HttpRequestOptionsKey<MySignCredentials> Credentials = new("ViettelConnect.MySign.Credentials");

    public static readonly HttpRequestOptionsKey<bool> SkipAuth = new("ViettelConnect.MySign.SkipAuth");

    /// <summary>Request không idempotent (vd signHash: mỗi lần gửi đẩy một lời nhắc xác nhận tới điện thoại người ký) nên không thử lại khi lỗi tạm thời.</summary>
    public static readonly HttpRequestOptionsKey<bool> NoRetry = new("ViettelConnect.MySign.NoRetry");
}
