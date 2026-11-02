---
title: Bắt đầu
layout: default
parent: MySign
nav_order: 1
---

# Bắt đầu

> Gói chưa có trên nuget.org. Hiện chỉ dùng được bằng tham chiếu project (`ViettelConnect.MySign.Client`).

## 1. Cấu hình

Mục `Viettel:MySign`. **Không ghi bí mật vào file cấu hình đã commit**; dùng user-secrets hoặc biến môi trường.

```json
{
  "Viettel": {
    "MySign": {
      "BaseUrl": "https://remotesigning.viettel.vn/vtss/service",
      "SignMode": "Polling",
      "Polling": { "Interval": "00:00:03", "TotalTimeout": "00:05:00" }
    }
  }
}
```

```
dotnet user-secrets set "Viettel:MySign:ClientId" "<client_id>"
dotnet user-secrets set "Viettel:MySign:ClientSecret" "<client_secret>"
dotnet user-secrets set "Viettel:MySign:ProfileId" "<profile_id>"
dotnet user-secrets set "Viettel:MySign:UserId" "<user_id>"
```

| Khoá | Ý nghĩa |
| --- | --- |
| `BaseUrl` | Mặc định `https://remotesigning.viettel.vn/vtss/service`. |
| `ClientId`, `ClientSecret`, `ProfileId`, `UserId` | Bắt buộc ở `CredentialsMode = Static` (mặc định). |
| `CredentialsMode` | `Static` hoặc `Dynamic` (lấy theo lời gọi qua `IMySignCredentialsAccessor`, dùng khi mỗi lab / mỗi người ký có thông tin riêng). |
| `SignMode` | `Polling` (mặc định) hoặc `Synchronous`. Callback dùng `BeginSignHashesAsync`. |
| `Polling:Interval`, `Polling:TotalTimeout` | Chu kỳ hỏi (3 giây) và tổng thời gian chờ người ký (5 phút). |
| `TransportRetry` | Số lần thử lại, độ trễ cho lỗi tạm thời (không áp dụng cho `signHash`). |
| `Errors:IncludeRawErrorMessage` | Chỉ bật khi chẩn đoán; cho thông điệp lỗi thô của Viettel xuất hiện. |
| `Callback:ClientId`, `ClientSecret`, `TokenLifetime` | Cặp client Viettel dùng khi gọi endpoint callback của bạn; token sống 1 giờ mặc định. |
| `Callback:PendingMaxAge` | Thời gian kho giao dịch chờ mặc định (trong bộ nhớ) giữ một giao dịch callback chưa có kết quả; mặc định 24 giờ. |
| `Advanced:PollingAsyncCode` | Tuỳ chọn ẩn, mặc định 2, chưa kiểm chứng (tài liệu Viettel ghi 1 ở một mục, 2 ở mục khác). |
| `Advanced:HttpTimeout` | Thời gian chờ một lời gọi HTTP; mặc định `Polling:TotalTimeout` + 1 phút, không bao giờ vô hạn. |

## 2. Đăng ký DI

```csharp
services.AddViettelMySign(configuration);                 // đọc mục Viettel:MySign
// hoặc
services.AddViettelMySign(o => { o.ClientId = "..."; /* ... */ });
```

Options được kiểm tra khi khởi động (`ValidateOnStart`). `IViettelMySignClient` là **scoped**.

## 3. Ký mã băm

```csharp
var hash = SHA256.HashData(await File.ReadAllBytesAsync(path));
var request = new SignHashesRequestDto(
    [new HashDocumentDto("doc-1", "Biên bản thí nghiệm máy biến áp", hash)],
    Description: "Ký biên bản");

var result = await client.SignHashesAsync(request);     // người ký bấm xác nhận trên app MySign
byte[] signature = result.Signatures[0].Signature;      // đúng thứ tự tài liệu
IReadOnlyList<byte[]> chain = result.Certificate.Chain; // DER, chứng thư người ký đứng đầu
```

`DisplayName` có dấu tiếng Việt được; SDK tự chuẩn hoá (bỏ dấu, `đ` thành `d`, cắt cho vừa giới hạn của Viettel). Ví dụ chạy được: `samples/ViettelConnect.MySign.Sample.Console`.

## 4. Chọn chứng thư

Mặc định SDK chọn chứng thư đầu tiên còn hiệu lực. Để chọn đích danh: `ListCertificatesAsync()` rồi truyền `CredentialId` vào `SignHashesRequestDto`.
