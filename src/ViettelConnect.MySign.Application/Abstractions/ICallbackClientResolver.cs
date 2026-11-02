namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>
/// Một cặp client mà Viettel dùng khi gọi API callback của đối tác, gắn với một đơn vị (lab/tenant).
/// <see cref="ToString"/> không bao giờ lộ <see cref="ClientSecret"/>.
/// </summary>
public sealed record CallbackClient(string TenantKey, string ClientId, string ClientSecret)
{
    public override string ToString() => $"CallbackClient {{ TenantKey = {TenantKey} }}";
}

/// <summary>Tìm client callback theo <c>client_id</c>. Mặc định: một client duy nhất từ options (TenantKey "default").</summary>
public interface ICallbackClientResolver
{
    /// <summary>Trả <c>null</c> nếu không có client nào khớp.</summary>
    ValueTask<CallbackClient?> FindAsync(string clientId, CancellationToken ct);
}
