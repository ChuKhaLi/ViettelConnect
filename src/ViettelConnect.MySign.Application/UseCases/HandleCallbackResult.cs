using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Application.Signing;
using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>
/// API nhận kết quả ký của đối tác (tài liệu Viettel mục 3.2). Đơn vị (tenant) chỉ được suy ra từ token đã xác thực;
/// giao dịch chờ được tra theo (tenant, transaction_id) nên token của lab này không hoàn tất được giao dịch của lab khác.
/// Mọi lỗi do đầu vào (token, JSON hỏng, giao dịch lạ, tập document không khớp) đều trả status_code khác "00" với
/// HTTP 200, không ném, không mô tả chi tiết. Lỗi của kho (ứng dụng tự cài <see cref="ICallbackTokenStore"/> hoặc
/// <see cref="IPendingSignatureStore"/> và nó ném ngoại lệ) không ném ra ngoài mà trả HTTP 500 với thân chung để Viettel
/// gửi lại; chỉ huỷ của người gọi mới được ném lại.
/// </summary>
internal sealed class HandleCallbackResult
{
    private const string InvalidTokenCode = "01";
    private const string InvalidTokenMessage = "invalid_token";
    private const string InvalidRequestCode = "02";
    private const string InvalidRequestMessage = "invalid_request";

    private readonly ICallbackTokenStore _tokens;
    private readonly IPendingSignatureStore _pending;
    private readonly ILogger _logger;

    public HandleCallbackResult(
        ICallbackTokenStore tokens,
        IPendingSignatureStore pending,
        ILogger<HandleCallbackResult>? logger = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    public async Task<(CallbackResponse Response, CallbackSignOutcome? Outcome)> ExecuteAsync(
        string? bearerToken,
        string requestJson,
        CancellationToken ct)
    {
        try
        {
            return await ExecuteCoreAsync(bearerToken, requestJson, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            // Chỉ ghi loại ngoại lệ: thông điệp của kho do ứng dụng viết có thể chứa token hay chuỗi kết nối.
            _logger.LogError(
                "MySign callback result handling failed in a store ({ExceptionType}); returning HTTP 500 so Viettel can retry.",
                ex.GetType().Name);
            return (CallbackJson.ServerError(), null);
        }
    }

    private async Task<(CallbackResponse Response, CallbackSignOutcome? Outcome)> ExecuteCoreAsync(
        string? bearerToken,
        string requestJson,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(bearerToken))
        {
            return Reject(InvalidTokenCode, InvalidTokenMessage);
        }

        var tenantKey = await _tokens.ValidateAsync(bearerToken, ct).ConfigureAwait(false);
        if (tenantKey is null)
        {
            return Reject(InvalidTokenCode, InvalidTokenMessage);
        }

        if (!TryParse(requestJson, out var body))
        {
            return Reject(InvalidRequestCode, InvalidRequestMessage);
        }

        var pending = await _pending.FindAsync(tenantKey, body.TransactionId, ct).ConfigureAwait(false);
        if (pending is null)
        {
            return Reject(InvalidRequestCode, InvalidRequestMessage);
        }

        var status = SignStatusCodes.Classify(body.Status);
        IReadOnlyList<DocumentSignature> signatures = [];

        switch (status)
        {
            case SignStatus.Succeeded:
                if (!TryMatchSignatures(pending, body.Results, out signatures))
                {
                    return Reject(InvalidRequestCode, InvalidRequestMessage);
                }

                break;

            case SignStatus.Pending:
                // Chưa phải kết cục cuối: ghi nhận nhưng giữ giao dịch chờ.
                return (CallbackJson.Success(), new CallbackSignOutcome(tenantKey, pending.TransactionId, status, signatures, body.Status));
        }

        // Chỉ người thắng cuộc đua xoá mới phát Outcome; bản gửi trùng nhận "00" (idempotent) nhưng không có Outcome.
        if (!await _pending.TryRemoveAsync(tenantKey, pending.TransactionId, ct).ConfigureAwait(false))
        {
            return (CallbackJson.Success(), null);
        }

        return (CallbackJson.Success(), new CallbackSignOutcome(tenantKey, pending.TransactionId, status, signatures, body.Status));
    }

    private static (CallbackResponse Response, CallbackSignOutcome? Outcome) Reject(string code, string message) =>
        (CallbackJson.Failure(code, message), null);

    /// <summary>
    /// SDK đánh số tài liệu 1..n theo thứ tự gửi (xem <see cref="SignHashesSupport"/>), nên document_id trong kết quả là
    /// số (hoặc chuỗi số) k tương ứng <c>pending.DocumentIds[k-1]</c>. Tập id phải khớp tuyệt đối: ngoài khoảng, không
    /// phải số, trùng hay thiếu đều bị từ chối. Trả chữ ký theo thứ tự đã gửi, kèm DocumentId của ứng dụng.
    /// </summary>
    private static bool TryMatchSignatures(
        PendingSignature pending,
        IReadOnlyList<(string DocumentId, string Signature)> results,
        out IReadOnlyList<DocumentSignature> signatures)
    {
        signatures = [];
        if (results.Count != pending.DocumentIds.Count)
        {
            return false;
        }

        var encodedByPosition = new string?[pending.DocumentIds.Count];
        foreach (var (id, signature) in results)
        {
            if (id.Length > 1 && id[0] == '0'
                || !int.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var wireId)
                || wireId < 1
                || wireId > encodedByPosition.Length
                || encodedByPosition[wireId - 1] is not null)
            {
                return false;
            }

            encodedByPosition[wireId - 1] = signature;
        }

        var ordered = new DocumentSignature[pending.DocumentIds.Count];
        for (var i = 0; i < ordered.Length; i++)
        {
            if (encodedByPosition[i] is not { } encoded)
            {
                return false;
            }

            try
            {
                ordered[i] = new DocumentSignature(pending.DocumentIds[i], Base64.Decode(encoded));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        signatures = ordered;
        return true;
    }

    private static bool TryParse(string? json, out ResultBody body)
    {
        body = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("meta_data", out var meta)
                || meta.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var transactionId = CallbackJson.ReadLooseString(meta, "transaction_id");
            if (string.IsNullOrWhiteSpace(transactionId))
            {
                return false;
            }

            var status = CallbackJson.ReadLooseString(meta, "status");
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            var results = new List<(string, string)>();
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("results", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    var id = CallbackJson.ReadLooseString(item, "document_id");
                    var signature = CallbackJson.ReadLooseString(item, "signature");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(signature))
                    {
                        return false;
                    }

                    results.Add((id, signature));
                }
            }

            body = new ResultBody(transactionId, status, results);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private readonly record struct ResultBody(
        string TransactionId,
        string? Status,
        IReadOnlyList<(string DocumentId, string Signature)> Results);
}
