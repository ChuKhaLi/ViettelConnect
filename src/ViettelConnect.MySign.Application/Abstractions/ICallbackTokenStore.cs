namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>
/// Kho token callback do đối tác cấp cho Viettel; mỗi token gắn với đúng một đơn vị và có hạn dùng.
/// <para>
/// Hợp đồng: với đầu vào lạ (token rỗng, sai, đã hết hạn) cài đặt <b>không được ném</b> mà trả <c>null</c>. Ngoại lệ chỉ
/// dành cho sự cố hạ tầng (vd mất kết nối CSDL); khi đó endpoint callback trả HTTP 500 với thân chung (không lộ chi tiết)
/// để Viettel gửi lại.
/// </para>
/// </summary>
public interface ICallbackTokenStore
{
    /// <summary>Sinh token ngẫu nhiên mật mã, gắn với <paramref name="tenantKey"/>, hết hạn sau <paramref name="lifetime"/>.</summary>
    ValueTask<string> IssueAsync(string tenantKey, TimeSpan lifetime, CancellationToken ct);

    /// <summary>Trả <c>tenantKey</c> của token còn hạn; <c>null</c> nếu token lạ hoặc đã hết hạn.</summary>
    ValueTask<string?> ValidateAsync(string token, CancellationToken ct);
}
