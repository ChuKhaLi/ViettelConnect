using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ViettelConnect.MySign.Application.Abstractions;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>
/// API xác thực của đối tác (tài liệu Viettel mục 3.1): Viettel gửi client_id/client_secret, đối tác trả access_token
/// dùng cho lần báo kết quả. Mọi thất bại do đầu vào (JSON hỏng, thiếu hoặc sai <c>grant_type</c> — bắt buộc đúng
/// <c>client_credentials</c>, client lạ, sai secret) đều trả cùng một phản hồi chung "01 invalid_client" (không lộ client
/// nào tồn tại, không lộ lý do). Lỗi của kho/resolver do ứng dụng cài (ném ngoại lệ) trả HTTP 500 với thân chung để
/// Viettel thử lại; chỉ huỷ của người gọi mới được ném lại.
/// </summary>
internal sealed class HandleCallbackTokenRequest
{
    private const string FailureCode = "01";
    private const string FailureMessage = "invalid_client";

    private readonly ICallbackClientResolver _clients;
    private readonly ICallbackTokenStore _tokens;
    private readonly TimeSpan _tokenLifetime;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    public HandleCallbackTokenRequest(
        ICallbackClientResolver clients,
        ICallbackTokenStore tokens,
        TimeSpan tokenLifetime,
        TimeProvider time,
        ILogger<HandleCallbackTokenRequest>? logger = null)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        if (tokenLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenLifetime), "Token lifetime must be > 0.");
        }

        _tokenLifetime = tokenLifetime;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    public async Task<CallbackResponse> ExecuteAsync(string requestJson, CancellationToken ct)
    {
        try
        {
            return await ExecuteCoreAsync(requestJson, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            // Chỉ ghi loại ngoại lệ: thông điệp của kho/resolver do ứng dụng viết có thể chứa secret hay chuỗi kết nối.
            _logger.LogError(
                "MySign callback token request failed in a store or client resolver ({ExceptionType}); returning HTTP 500 so Viettel can retry.",
                ex.GetType().Name);
            return CallbackJson.ServerError();
        }
    }

    private async Task<CallbackResponse> ExecuteCoreAsync(string requestJson, CancellationToken ct)
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

            // Tài liệu Viettel (mục 3.1) luôn gửi grant_type = client_credentials: thiếu hoặc khác đều bị từ chối.
            var grant = CallbackJson.ReadLooseString(root, "grant_type");
            if (!string.Equals(grant, "client_credentials", StringComparison.Ordinal))
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
