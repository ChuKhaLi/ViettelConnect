using ViettelConnect.MySign.Domain.Certificates;

namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>Giao dịch ký bất đồng bộ (callback) đang chờ Viettel báo kết quả.</summary>
public sealed record PendingSignature(
    string TransactionId,
    string TenantKey,
    Certificate Certificate,
    IReadOnlyList<string> DocumentIds,
    DateTimeOffset CreatedAt)
{
    public override string ToString() => $"PendingSignature {{ TenantKey = {TenantKey}, Documents = {DocumentIds.Count} }}";
}

/// <summary>Kho giao dịch chờ, khoá theo (tenantKey, transactionId) để các lab không thấy giao dịch của nhau.</summary>
public interface IPendingSignatureStore
{
    ValueTask SaveAsync(PendingSignature p, CancellationToken ct);

    ValueTask<PendingSignature?> FindAsync(string tenantKey, string transactionId, CancellationToken ct);

    /// <summary>Xoá nguyên tử; trả <c>true</c> chỉ cho đúng một người gọi thắng (dùng để chống hoàn tất trùng lặp).</summary>
    ValueTask<bool> TryRemoveAsync(string tenantKey, string transactionId, CancellationToken ct);
}
