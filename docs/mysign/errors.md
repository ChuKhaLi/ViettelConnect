---
title: Lỗi
layout: default
parent: MySign
nav_order: 4
---

# Lỗi

Lỗi nghiệp vụ / giao thức ném `MySignException` (hết giờ chờ: `MySignTimeoutException`). Thuộc tính: `Category`, `RawCode` (mã Viettel nếu có), `Detail`, `CorrelationId`.

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
| `Transport` | Lỗi kết nối | Có thể thử lại (trừ khi đã gửi `signHash`). |
| `ProtocolViolation` | Phản hồi sai hợp đồng (ví dụ thiếu chữ ký) | Báo lỗi, không dùng kết quả. |
| `CallbackValidation` | Callback không hợp lệ | Xem [Callback](./callback). |
| `Unknown` | Mã không có trong bảng | Bật chẩn đoán để xem mã gốc. |

Bảng ánh xạ mã Viettel sang nhóm nằm ở `MySignErrorCatalog` (Domain), lập theo tài liệu Viettel v1.8 (Phụ lục 1, danh sách mã lỗi chung, và bảng mã trạng thái `meta_data.status`).

## Không bao giờ có trong lỗi / log

`client_secret`, access token, chữ ký, token callback và định danh người ký.
