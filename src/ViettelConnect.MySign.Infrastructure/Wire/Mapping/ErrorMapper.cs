using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Infrastructure.Logging;

namespace ViettelConnect.MySign.Infrastructure.Wire.Mapping;

internal static class ErrorMapper
{
    private const int MaxRawLength = 2000;

    /// <summary>
    /// Dựng <see cref="MySignException"/> từ phản hồi lỗi (non-2xx). Thông điệp mặc định chỉ chứa tên thao tác,
    /// mã HTTP và mã lỗi; thân thô của Viettel chỉ được thêm khi <paramref name="includeRaw"/> và đã che
    /// <paramref name="secret"/>. Nhóm <see cref="MySignErrorCategory.SignTimeout"/> trả <see cref="MySignTimeoutException"/>;
    /// 5xx/429 không có mã Viettel nhận diện được là <see cref="MySignErrorCategory.Transport"/>.
    /// </summary>
    public static MySignException FromHttpError(
        string operation,
        int httpStatus,
        ErrorDto? dto,
        string? rawBody,
        bool includeRaw,
        string secret,
        string correlationId)
    {
        var code = FirstNonEmpty(dto?.Error, dto?.ErrorCode);
        var category = code is not null
            ? MySignErrorCatalog.Classify(code)
            : httpStatus switch
            {
                401 or 403 => MySignErrorCategory.Authentication,
                400 => MySignErrorCategory.Validation,
                _ => MySignErrorCategory.Unknown,
            };

        // Mã chuỗi lạ (vd "server_error") vẫn thuộc Unknown nhưng 401/403 vẫn là lỗi xác thực.
        if (category == MySignErrorCategory.Unknown && httpStatus is 401 or 403)
        {
            category = MySignErrorCategory.Authentication;
        }

        // 5xx/429 không kèm mã Viettel nhận diện được (handler retry đã trả phản hồi của lần thử cuối): lỗi kết nối/máy chủ.
        if (category == MySignErrorCategory.Unknown && (httpStatus >= 500 || httpStatus == 429))
        {
            category = MySignErrorCategory.Transport;
        }

        var detail = $"MySign {operation} failed (HTTP {httpStatus}, code {code ?? "<none>"}).";
        if (includeRaw && !string.IsNullOrEmpty(rawBody))
        {
            detail += " Raw response: " + Scrub(rawBody, secret);
        }

        // Hợp đồng lỗi thống nhất giữa các chế độ ký: hết giờ chờ người ký luôn là MySignTimeoutException.
        return category == MySignErrorCategory.SignTimeout
            ? new MySignTimeoutException(detail, correlationId, transactionId: null, rawCode: code)
            : new MySignException(category, code, detail, correlationId);
    }

    public static MySignException Protocol(string operation, string what, string correlationId, Exception? inner = null) =>
        new(MySignErrorCategory.ProtocolViolation, null, $"MySign {operation}: unexpected response ({what}).", correlationId, inner);

    public static MySignException Transport(string operation, Exception inner, string correlationId) =>
        new(MySignErrorCategory.Transport, null, $"MySign {operation}: transport failure ({inner.GetType().Name}).", correlationId, inner);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string Scrub(string raw, string secret)
    {
        // Che đúng secret của lời gọi trước (kể cả khi nằm ngoài JSON), rồi áp bộ che chung (access token, Bearer...).
        var text = string.IsNullOrEmpty(secret) ? raw : raw.Replace(secret, MySignLogScrubber.Mask, StringComparison.Ordinal);
        text = MySignLogScrubber.Scrub(text);
        return text.Length <= MaxRawLength ? text : text[..MaxRawLength] + "...";
    }
}
