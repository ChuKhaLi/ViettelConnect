using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ViettelConnect.MySign.Application.Abstractions;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>
/// API xác thực của đối tác (tài liệu Viettel mục 3.1): Viettel gửi client_id/client_secret, đối tác trả access_token
/// dùng cho lần báo kết quả. Mọi thất bại đều trả cùng một phản hồi chung (không lộ client nào tồn tại, không lộ lý do).
/// </summary>
internal sealed class HandleCallbackTokenRequest
{
    private const string FailureCode = "01";
    private const string FailureMessage = "invalid_client";

    private readonly ICallbackClientResolver _clients;
    private readonly ICallbackTokenStore _tokens;
    private readonly TimeSpan _tokenLifetime;
    private readonly TimeProvider _time;

    public HandleCallbackTokenRequest(
        ICallbackClientResolver clients,
        ICallbackTokenStore tokens,
        TimeSpan tokenLifetime,
        TimeProvider time)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        if (tokenLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenLifetime), "Token lifetime must be > 0.");
        }

        _tokenLifetime = tokenLifetime;
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task<CallbackResponse> ExecuteAsync(string requestJson, CancellationToken ct)
    {
        if (!TryParse(requestJson, out var clientId, out var clientSecret))
        {
            return CallbackJson.Failure(FailureCode, FailureMessage);
        }

        var client = await _clients.FindAsync(clientId, ct).ConfigureAwait(false);

        // So khớp luôn chạy (kể cả khi không tìm thấy client) để thời gian phản hồi không tiết lộ client_id có tồn tại.
        var secretMatches = SecretEquals(clientSecret, client?.ClientSecret ?? string.Empty);
        if (client is null || !secretMatches)
        {
            return CallbackJson.Failure(FailureCode, FailureMessage);
        }

        var token = await _tokens.IssueAsync(client.TenantKey, _tokenLifetime, ct).ConfigureAwait(false);
        return CallbackJson.TokenSuccess(
            token,
            (long)_tokenLifetime.TotalMilliseconds,
            _time.GetUtcNow().ToUnixTimeSeconds());
    }

    /// <summary>So sánh thời gian cố định trên băm SHA-256 của hai chuỗi (độ dài khác nhau cũng không rò rỉ).</summary>
    private static bool SecretEquals(string provided, string expected)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b) && expected.Length > 0;
    }

    private static bool TryParse(string? json, out string clientId, out string clientSecret)
    {
        clientId = string.Empty;
        clientSecret = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var grant = CallbackJson.ReadLooseString(root, "grant_type");
            if (grant is not null && !string.Equals(grant, "client_credentials", StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty("client_id", out var id) || id.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("client_secret", out var secret) || secret.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            clientId = id.GetString() ?? string.Empty;
            clientSecret = secret.GetString() ?? string.Empty;
            return clientId.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
