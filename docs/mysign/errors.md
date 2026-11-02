---
title: Lỗi
layout: default
parent: MySign
nav_order: 4
---

# Lỗi

Lỗi nghiệp vụ / giao thức ném `MySignException`. Thuộc tính: `Category`, `RawCode` (mã Viettel nếu có), `Detail`, `CorrelationId`, `TransactionId`.

- **Hết giờ chờ người ký** luôn là `MySignTimeoutException` (lớp con, `Category = SignTimeout`) ở mọi chế độ của `SignHashesAsync`: đồng bộ (Viettel trả mã 58061), hỏi trạng thái (trạng thái 4001 hoặc hết `Polling:TotalTimeout`). Bắt `MySignTimeoutException` trước `MySignException`.
- **`TransactionId`** có giá trị khi lỗi xảy ra *sau* khi Viettel đã nhận yêu cầu ký (người ký có thể đã thấy lời nhắc): từ chối, ký thất bại, vi phạm giao thức, hết giờ khi hỏi trạng thái, hoặc không lưu được giao dịch chờ callback. Dùng để đối soát; null nếu lỗi xảy ra trước khi gửi.
- **`CorrelationId`** là mã của lời gọi gây lỗi, kể cả lỗi do pipeline HTTP ném (thử lại hết lượt, 401 lần hai, đăng nhập lại) khi nhiều lời gọi chạy song song.

Mã lỗi Viettel trong thân phản hồi được ưu tiên hơn mã HTTP: ví dụ `signHash` trả HTTP 500 kèm `{"error":"58083"}` là `SignFailed`, không phải `Transport`.

Thông điệp lỗi thô của Viettel **không** nằm trong `Detail` trừ khi bật `Errors:IncludeRawErrorMessage = true` (chỉ để chẩn đoán).

| `Category` | Ý nghĩa | Gợi ý xử lý |
| --- | --- | --- |
| `Authentication` | Sai client / secret / profile / user / token | Kiểm tra cấu hình, đừng thử lại mù quáng. |
| `Validation` | Thiếu hoặc sai tham số | Sửa yêu cầu. |
| `NoActiveCertificate` | Không có chứng thư còn hiệu lực | Kiểm tra tài khoản MySign. |
| `CertificateInvalid` | Khoá hoặc chuỗi chứng thư có vấn đề | Liên hệ Viettel-CA. |
| `SignRejected` | Người ký từ chối | Báo cho người dùng. |
| `SignTimeout` | Người ký không xác nhận kịp | Cho phép ký lại. |
| `SignFailed` | Ký thất bại phía Viettel | Thử lại có chủ đích. |
| `InsufficientQuota` | Hết lượt ký | Liên hệ Viettel-CA. |
| `Transport` | Lỗi kết nối, hết giờ HTTP, hoặc 5xx/429 không kèm mã Viettel (sau khi đã thử lại ngắn) | Có thể thử lại — trừ khi lỗi xảy ra ở chính lời gọi `signHash` (khi đó Viettel có thể đã nhận yêu cầu và điện thoại người ký đã hiện lời nhắc; thử lại sẽ tạo yêu cầu thứ hai). Khi hỏi trạng thái, lỗi kết nối tạm thời không ném ra mà được coi là đang chờ tới hạn chót. |
| `ProtocolViolation` | Phản hồi sai hợp đồng (ví dụ thiếu chữ ký) | Báo lỗi, không dùng kết quả. |
| `CallbackValidation` | Dự phòng — hiện SDK **không** ném nhóm này. Callback sai (token hết hạn/sai, giao dịch không biết, thân hỏng) được báo bằng `status_code` khác `"00"` trong phản hồi callback, không bằng ngoại lệ. | Xem [Callback](./callback). |
| `Unknown` | Mã không có trong bảng (lỗi 4xx), hoặc không lưu được giao dịch chờ callback (`InnerException` là lỗi gốc của kho) | Bật chẩn đoán để xem mã gốc. |

Bảng ánh xạ mã Viettel sang nhóm nằm ở `MySignErrorCatalog` (Domain), lập theo tài liệu Viettel v1.8 (Phụ lục 1, danh sách mã lỗi chung, và bảng mã trạng thái `meta_data.status`).

## Không bao giờ có trong lỗi / log

`client_secret`, access token, chữ ký, token callback và định danh người ký.
