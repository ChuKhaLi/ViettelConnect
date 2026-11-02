---
title: Kiến trúc
layout: default
nav_order: 2
---

# Kiến trúc

Bộ khung chép từ `MisaConnect.ESign` (cùng tác giả): bốn lớp, phụ thuộc một chiều.

| Lớp | Nội dung | Phụ thuộc |
| --- | --- | --- |
| `ViettelConnect.MySign.Domain` | Lỗi (`MySignException`, nhóm lỗi, bảng mã lỗi), kiểu giá trị, `SignMode` | Không |
| `ViettelConnect.MySign.Application` | Use case (liệt kê chứng thư, ký, bắt đầu ký, xử lý callback) và các cổng | Domain, `Logging.Abstractions` |
| `ViettelConnect.MySign.Infrastructure` | Wire client, `HttpClient` + handler, mặc định của cổng, options, DI | Application |
| `ViettelConnect.MySign.Client` | Facade `IViettelMySignClient`, DTO công khai, `AddViettelMySign` (gói NuGet `ViettelConnect.MySign`) | Các lớp trên |

## Chuỗi HTTP

`MySignCallLogger` bọc wire client (decorator, nằm ngoài `HttpClient`): ghi log mỗi lời gọi theo endpoint, thời gian, kết quả, nhóm lỗi — không ghi tham số, bí mật hay chữ ký.

Bên trong `HttpClient`, thứ tự handler là: `TransientFailureRetryHandler` (ngoài) → `MySignAuthHandler` (trong) → mạng.

- `TransientFailureRetryHandler`: thử lại có giới hạn (lùi mũ + jitter) với 5xx, 429, lỗi mạng, hết giờ. Lần thử cuối mà vẫn 5xx/429 thì trả phản hồi đó cho wire client để phân loại theo mã lỗi Viettel trong thân (không có mã thì là `Transport`). `signHash` **không** retry lỗi mạng/5xx vì mỗi lần gọi làm điện thoại người ký hiện yêu cầu.
- `MySignAuthHandler`: gắn Bearer (token cache có single-flight refresh, khoá theo `client_id` + người ký nên hai lab chạy song song không dùng nhầm token); gặp 401 thì đăng nhập lại đúng một lần rồi gửi lại. Khoá cache (`MySignCredentials.CacheKey`) là băm SHA-256 nên `ITokenCache` (kể cả cache phân tán) không thấy định danh người ký ở dạng thường. Token có thời hạn không dài hơn biên hết hạn sớm (60 giây) dùng biên bằng nửa thời hạn để vẫn được dùng lại, kèm một cảnh báo log cho mỗi khoá.

Mã tương quan (correlation id) của người gọi đi kèm từng request (`HttpRequestMessage.Options`), nên lỗi do handler ném luôn mang đúng mã của lời gọi gốc kể cả khi nhiều lời gọi chạy song song. Ngoại lệ: lượt đăng nhập single-flight được **dùng chung** giữa các request cùng khoá (cùng đơn vị + người ký) đang chờ, nên log của lượt đăng nhập đó và lỗi nó ném (vd sai `client_secret`) mang mã tương quan của request **khởi phát** lượt đăng nhập — các request cùng khoá chờ chung nhận cùng ngoại lệ ấy, với mã của request khởi phát. Lỗi handler ném sau đó (vd 401 lần hai) vẫn mang mã của từng request.

## Cổng thay được

Token cache, nguồn thông tin đăng nhập (`IMySignCredentialsAccessor`), chọn chứng thư, kho phiên ký chờ callback, đồng hồ/độ trễ. Mặc định đăng ký bằng `TryAdd`, ứng dụng ghi đè được.

## Ràng buộc cứng

- Phụ thuộc chỉ `Microsoft.Extensions.*` và `System.Text.Json`; không thư viện PDF, không AGPL/GPL, không Polly/Refit.
- Không bao giờ log hay in `client_secret`, access token, chữ ký, định danh người ký. Thân lỗi thô của Viettel chỉ hiện khi bật `Errors.IncludeRawErrorMessage`.
- API công khai không lộ chi tiết wire (mã `async`, OID, tên tài liệu base64).
- Chưa có tài khoản Viettel: kiểm chứng bằng `FakeMySignServer` trong `tests/ViettelConnect.MySign.IntegrationTests`.
