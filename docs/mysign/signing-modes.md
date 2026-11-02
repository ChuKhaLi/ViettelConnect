---
title: Các kiểu ký
layout: default
parent: MySign
nav_order: 2
---

# Các kiểu ký

Cả ba kiểu đều chỉ ký mã băm và đều cần người ký xác nhận trên ứng dụng MySign. **Chưa kiểu nào được kiểm chứng với MySign thật.**

| Kiểu | Cấu hình / gọi | Hành vi |
| --- | --- | --- |
| Đồng bộ | `SignMode = Synchronous`, `SignHashesAsync` | Một lời gọi `signHash` chờ tới khi người ký xác nhận. Thời gian chờ HTTP lấy từ `Advanced:HttpTimeout` (mặc định `Polling:TotalTimeout` + 1 phút). Không thử lại khi lỗi mạng/5xx vì sẽ gửi yêu cầu thứ hai tới điện thoại người ký. |
| Hỏi trạng thái | `SignMode = Polling` (mặc định), `SignHashesAsync` | Gửi yêu cầu rồi hỏi `requests/status` mỗi `Polling:Interval` tới hết `Polling:TotalTimeout`. Trạng thái lạ hoặc rỗng được coi là đang chờ; hết giờ thì ném `MySignTimeoutException`, không treo vô hạn. |
| Callback | `BeginSignHashesAsync` + hai endpoint callback | Trả ngay `TransactionId`; Viettel báo kết quả qua endpoint của ứng dụng. Xem [Callback](./callback). |

## Thứ tự và tính toàn vẹn

Chữ ký trả về cùng thứ tự tài liệu của yêu cầu. Nếu Viettel trả số chữ ký khác số mã băm, SDK ném `MySignException` loại `ProtocolViolation` thay vì trả kết quả lệch.

## Giá trị `async` là chi tiết nội bộ

Mã `async` không lộ ra API công khai. Riêng kiểu hỏi trạng thái dùng giá trị mặc định 2 vì tài liệu Viettel tự mâu thuẫn; đổi bằng `Advanced:PollingAsyncCode` nếu Viettel xác nhận khác.
