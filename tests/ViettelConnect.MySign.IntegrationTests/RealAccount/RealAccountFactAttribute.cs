using Xunit;

namespace ViettelConnect.MySign.IntegrationTests.RealAccount;

/// <summary>
/// Test chạy với tài khoản Viettel MySign thật; tự bỏ qua (Skipped, không Failed) khi thiếu một trong bốn biến môi trường
/// <c>VIETTEL_MYSIGN_CLIENT_ID</c>, <c>VIETTEL_MYSIGN_CLIENT_SECRET</c>, <c>VIETTEL_MYSIGN_PROFILE_ID</c>, <c>VIETTEL_MYSIGN_USER_ID</c>.
/// </summary>
public sealed class RealAccountFactAttribute : FactAttribute
{
    public const string ClientIdVar = "VIETTEL_MYSIGN_CLIENT_ID";
    public const string ClientSecretVar = "VIETTEL_MYSIGN_CLIENT_SECRET";
    public const string ProfileIdVar = "VIETTEL_MYSIGN_PROFILE_ID";
    public const string UserIdVar = "VIETTEL_MYSIGN_USER_ID";

    private static readonly string[] RequiredVars = [ClientIdVar, ClientSecretVar, ProfileIdVar, UserIdVar];

    public RealAccountFactAttribute()
    {
        var missing = RequiredVars.Where(v => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v))).ToArray();
        if (missing.Length > 0)
        {
            Skip = $"Thiếu biến môi trường tài khoản MySign thật: {string.Join(", ", missing)}.";
        }
    }
}
