using System.Text.Encodings.Web;
using System.Text.Json;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>Dựng phản hồi JSON đúng định dạng Viettel chờ (status_code/message[/data]) và đọc trường lỏng kiểu.</summary>
internal static class CallbackJson
{
    private const string SuccessCode = "00";
    private const string SuccessMessage = "Thành công";
    private const string ServerErrorCode = "99";
    private const string ServerErrorMessage = "server_error";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // Giữ nguyên chữ có dấu ("Thành công") thay vì \uXXXX.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static CallbackResponse Success() => Build(SuccessCode, SuccessMessage, null);

    /// <summary>
    /// Lỗi nội bộ phía đối tác (vd kho của ứng dụng ném lỗi): HTTP 500 với thân chung, không kèm chi tiết, để Viettel
    /// gửi lại sau.
    /// </summary>
    public static CallbackResponse ServerError() => Build(ServerErrorCode, ServerErrorMessage, null, httpStatus: 500);

    /// <summary>Phản hồi lỗi chung, HTTP 200 (Viettel dừng luồng khi status_code khác "00"); không mô tả lý do chi tiết.</summary>
    public static CallbackResponse Failure(string code, string message) => Build(code, message, null);

    public static CallbackResponse TokenSuccess(string accessToken, long expiresInMs, long consentedOnUnixSeconds) =>
        Build(SuccessCode, SuccessMessage, w =>
        {
            w.WriteStartObject("data");
            w.WriteString("access_token", accessToken);
            w.WriteString("expires_in", expiresInMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            w.WriteString("consented_on", consentedOnUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            w.WriteEndObject();
        });

    /// <summary>Đọc chuỗi, chấp nhận cả số (Viettel có thể gửi status/transaction_id dạng số).</summary>
    public static string? ReadLooseString(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static CallbackResponse Build(string code, string message, Action<Utf8JsonWriter>? writeData, int httpStatus = 200)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("status_code", code);
            w.WriteString("message", message);
            writeData?.Invoke(w);
            w.WriteEndObject();
        }

        return new CallbackResponse(httpStatus, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
