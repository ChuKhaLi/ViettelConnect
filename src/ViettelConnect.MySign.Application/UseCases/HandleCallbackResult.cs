using System.Text.Json;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Application.Signing;
using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>
/// API nhận kết quả ký của đối tác (tài liệu Viettel mục 3.2). Đơn vị (tenant) chỉ được suy ra từ token đã xác thực;
/// giao dịch chờ được tra theo (tenant, transaction_id) nên token của lab này không hoàn tất được giao dịch của lab khác.
/// Mọi lỗi (token, JSON hỏng, giao dịch lạ, tập document không khớp) đều trả status_code khác "00" với HTTP 200,
/// không ném, không mô tả chi tiết.
/// </summary>
internal sealed class HandleCallbackResult
{
    private const string InvalidTokenCode = "01";
    private const string InvalidTokenMessage = "invalid_token";
    private const string InvalidRequestCode = "02";
    private const string InvalidRequestMessage = "invalid_request";

    private readonly ICallbackTokenStore _tokens;
    private readonly IPendingSignatureStore _pending;

    public HandleCallbackResult(ICallbackTokenStore tokens, IPendingSignatureStore pending)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
    }

    public async Task<(CallbackResponse Response, CallbackSignOutcome? Outcome)> ExecuteAsync(
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

        await _pending.RemoveAsync(tenantKey, pending.TransactionId, ct).ConfigureAwait(false);
        return (CallbackJson.Success(), new CallbackSignOutcome(tenantKey, pending.TransactionId, status, signatures, body.Status));
    }

    private static (CallbackResponse Response, CallbackSignOutcome? Outcome) Reject(string code, string message) =>
        (CallbackJson.Failure(code, message), null);

    /// <summary>Tập document_id trong kết quả phải khớp tuyệt đối với tập đã gửi; trả chữ ký theo thứ tự đã gửi.</summary>
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

        var byId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, signature) in results)
        {
            if (!byId.TryAdd(id, signature))
            {
                return false;
            }
        }

        var ordered = new DocumentSignature[pending.DocumentIds.Count];
        for (var i = 0; i < ordered.Length; i++)
        {
            if (!byId.TryGetValue(pending.DocumentIds[i], out var encoded))
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
