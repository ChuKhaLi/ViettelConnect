namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>Kho token callback do đối tác cấp cho Viettel; mỗi token gắn với đúng một đơn vị và có hạn dùng.</summary>
public interface ICallbackTokenStore
{
    /// <summary>Sinh token ngẫu nhiên mật mã, gắn với <paramref name="tenantKey"/>, hết hạn sau <paramref name="lifetime"/>.</summary>
    ValueTask<string> IssueAsync(string tenantKey, TimeSpan lifetime, CancellationToken ct);

    /// <summary>Trả <c>tenantKey</c> của token còn hạn; <c>null</c> nếu token lạ hoặc đã hết hạn.</summary>
    ValueTask<string?> ValidateAsync(string token, CancellationToken ct);
}
