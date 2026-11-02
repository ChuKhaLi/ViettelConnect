// Ví dụ minimal API cho ký có callback. Thông tin đăng nhập (Viettel:MySign:ClientId/ClientSecret/ProfileId/UserId và
// Viettel:MySign:Callback:ClientId/ClientSecret) đọc từ user-secrets hoặc biến môi trường, không ghi vào file.
//   POST /viettel/sign                 {"documentName": "...", "sha256Base64": "..."} -> bắt đầu ký, trả transactionId
//   GET|POST /viettel/callback/token   Viettel gọi để lấy token callback
//   GET|POST /viettel/callback/result  Viettel gọi để báo kết quả ký
//
// !!! CHỈ ĐỂ DEMO: /viettel/sign KHÔNG có xác thực/phân quyền — ai gọi được endpoint này đều đẩy được lời nhắc ký lên
// điện thoại của người ký đã cấu hình. Ứng dụng thật phải bảo vệ nó (RequireAuthorization, kiểm tra người dùng hiện tại
// đúng là người ký, chống lạm dụng). Hai endpoint callback do Viettel gọi và đã tự xác thực bằng client/token callback.
using System.Text;
using ViettelConnect.MySign.Client;
using ViettelConnect.MySign.Client.DependencyInjection;
using ViettelConnect.MySign.Client.Dtos;
using ViettelConnect.MySign.Domain.Errors;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddViettelMySign(builder.Configuration);

var app = builder.Build();

// DEMO ONLY — không xác thực. Ứng dụng thật: .RequireAuthorization() và kiểm tra quyền ký của người dùng.
app.MapPost("/viettel/sign", async (SignRequest body, IViettelMySignClient client, CancellationToken ct) =>
{
    try
    {
        var hash = Convert.FromBase64String(body.Sha256Base64);
        var request = new SignHashesRequestDto([new HashDocumentDto("doc-1", body.DocumentName, hash)]);
        var begin = await client.BeginSignHashesAsync(request, ct: ct);
        return Results.Ok(new { transactionId = begin.TransactionId });
    }
    catch (FormatException)
    {
        return Results.BadRequest(new { error = "sha256Base64 không phải base64 hợp lệ." });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (MySignException ex) when (ex.Category == MySignErrorCategory.Validation)
    {
        // Lỗi đầu vào của chính người gọi (vd mã băm không đủ 32 byte): 400, không phải lỗi của Viettel.
        return Results.BadRequest(new { error = ex.Detail });
    }
    catch (MySignException ex)
    {
        return Results.Problem(
            title: "Lỗi MySign",
            detail: $"[{ex.Category}] {ex.Detail}",
            statusCode: StatusCodes.Status502BadGateway);
    }
});

// Tài liệu Viettel ghi GET nhưng minh hoạ thân JSON: nhận cả GET lẫn POST và đọc thân bất kể phương thức.
app.MapMethods("/viettel/callback/token", ["GET", "POST"], async (HttpRequest http, IViettelMySignClient client, CancellationToken ct) =>
{
    var json = await ReadBodyAsync(http, ct);
    var response = await client.HandleCallbackTokenRequestAsync(json, ct);
    return Results.Content(response.JsonBody, "application/json", Encoding.UTF8, response.HttpStatus);
});

app.MapMethods("/viettel/callback/result", ["GET", "POST"], async (
    HttpRequest http,
    IViettelMySignClient client,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    var json = await ReadBodyAsync(http, ct);
    var (response, outcome) = await client.HandleCallbackResultAsync(BearerOf(http), json, ct);
    if (outcome is not null)
    {
        // Kết cục chỉ phát một lần: ứng dụng thật sẽ lưu chữ ký / báo cho người dùng ở đây.
        logger.LogInformation("Giao dịch {TransactionId} kết thúc: {Status}", outcome.TransactionId, outcome.Status);
    }

    return Results.Content(response.JsonBody, "application/json", Encoding.UTF8, response.HttpStatus);
});

app.Run();

static async Task<string> ReadBodyAsync(HttpRequest http, CancellationToken ct)
{
    using var reader = new StreamReader(http.Body, Encoding.UTF8);
    return await reader.ReadToEndAsync(ct);
}

static string? BearerOf(HttpRequest http)
{
    const string prefix = "Bearer ";
    var header = http.Headers.Authorization.ToString();
    return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : null;
}

internal sealed record SignRequest(string DocumentName, string Sha256Base64);

/// <summary>Điểm vào có thể tham chiếu được từ test (WebApplicationFactory<Program>).</summary>
public partial class Program;
