---
title: Callback
layout: default
parent: MySign
nav_order: 3
---

# Callback

Ở kiểu callback, **ứng dụng của bạn là máy chủ**: Viettel gọi hai endpoint do bạn host. SDK không phụ thuộc ASP.NET Core; bạn nối endpoint vào facade.

## Luồng

1. Đăng ký URL callback và cặp client callback với Viettel (cấu hình `Viettel:MySign:Callback:ClientId/ClientSecret`).
2. `BeginSignHashesAsync(request, tenantKey)` gửi yêu cầu và trả `TransactionId`. `tenantKey` (mặc định `"default"`) là đơn vị sở hữu giao dịch; phải khớp đơn vị của client callback.
3. Viettel gọi endpoint token: `HandleCallbackTokenRequestAsync(json)` kiểm client callback và phát token.
4. Viettel gọi endpoint kết quả kèm `Authorization: Bearer`: `HandleCallbackResultAsync(bearer, json)` trả `(Response, Outcome)`.
5. Ghi `Response` (`HttpStatus` + `JsonBody`, `application/json`) nguyên văn cho Viettel. Khi `Outcome` khác null, xử lý kết cục (`Succeeded` có `Signatures`, hoặc `Rejected`/`TimedOut`/`Failed`).

## Bảo đảm

- Input sai, token hết hạn hoặc sai, `transaction_id` lạ: trả `status_code` khác `"00"`, không ném lỗi, không lộ chi tiết.
- Kết cục cuối chỉ phát **đúng một lần** cho mỗi giao dịch (gỡ phiên chờ nguyên tử). Bản gửi trùng sau khi hoàn tất nhận `status_code` `"02"` và `Outcome = null`.
- `CallbackResponseDto.ToString()` và `DocumentSignatureDto.ToString()` không in nội dung nhạy cảm; đừng log thân phản hồi (có thể chứa token).

## Nối vào ASP.NET Core

Xem `samples/ViettelConnect.MySign.Sample.Api`. Tài liệu Viettel ghi GET nhưng minh hoạ thân JSON, nên ví dụ nhận cả GET lẫn POST và đọc thân bất kể phương thức:

```csharp
app.MapMethods("/viettel/callback/result", ["GET", "POST"], async (HttpRequest http, IViettelMySignClient client, CancellationToken ct) =>
{
    var json = await new StreamReader(http.Body).ReadToEndAsync(ct);
    var header = http.Headers.Authorization.ToString();
    var bearer = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    var (response, outcome) = await client.HandleCallbackResultAsync(bearer, json, ct);
    // lưu outcome nếu khác null
    return Results.Content(response.JsonBody, "application/json", Encoding.UTF8, response.HttpStatus);
});
```

## Lưu ý vận hành

- Kho phiên chờ mặc định nằm trong bộ nhớ: mất khi tiến trình khởi động lại và không chia sẻ giữa nhiều instance. Thay cổng kho phiên nếu cần chạy nhiều instance.
- Các điểm chưa rõ về callback (GET hay POST, mã HTTP khi lỗi token, Viettel có gửi lại hay không) nằm trong mục câu hỏi gửi Viettel ở README.
