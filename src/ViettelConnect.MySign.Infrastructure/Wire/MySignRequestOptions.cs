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
}
