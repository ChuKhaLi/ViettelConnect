using ViettelConnect.MySign.Domain.Errors;

namespace ViettelConnect.MySign.Infrastructure.Wire.Mapping;

internal static class ErrorMapper
{
    private const int MaxRawLength = 2000;

    /// <summary>
    /// Dựng <see cref="MySignException"/> từ phản hồi lỗi (non-2xx). Thông điệp mặc định chỉ chứa tên thao tác,
    /// mã HTTP và mã lỗi; thân thô của Viettel chỉ được thêm khi <paramref name="includeRaw"/> và đã che
    /// <paramref name="secret"/>.
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

        var detail = $"MySign {operation} failed (HTTP {httpStatus}, code {code ?? "<none>"}).";
        if (includeRaw && !string.IsNullOrEmpty(rawBody))
        {
            detail += " Raw response: " + Scrub(rawBody, secret);
        }

        return new MySignException(category, code, detail, correlationId);
    }

    public static MySignException Protocol(string operation, string what, string correlationId, Exception? inner = null) =>
        new(MySignErrorCategory.ProtocolViolation, null, $"MySign {operation}: unexpected response ({what}).", correlationId, inner);

    public static MySignException Transport(string operation, Exception inner, string correlationId) =>
        new(MySignErrorCategory.Transport, null, $"MySign {operation}: transport failure ({inner.GetType().Name}).", correlationId, inner);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string Scrub(string raw, string secret)
    {
        var text = string.IsNullOrEmpty(secret) ? raw : raw.Replace(secret, "<redacted>", StringComparison.Ordinal);
        return text.Length <= MaxRawLength ? text : text[..MaxRawLength] + "...";
    }
}
