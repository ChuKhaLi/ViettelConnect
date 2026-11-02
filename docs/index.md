---
title: Trang chủ
layout: default
nav_order: 1
description: "Community .NET SDK cho chữ ký số từ xa Viettel MySign: ký mã băm SHA-256."
permalink: /
---

# ViettelConnect
{: .fs-9 }

Community .NET SDK cho dịch vụ chữ ký số từ xa **Viettel MySign**: ứng dụng gửi mã băm SHA-256, người ký xác nhận trên ứng dụng MySign, SDK trả chữ ký thô và chuỗi chứng thư.
{: .fs-5 .fw-300 }

> **Chưa phát hành, chưa kiểm chứng với tài khoản Viettel thật.** Chưa có gói trên nuget.org. Toàn bộ test hiện chạy với máy chủ MySign giả trong repo. Đừng coi bất kỳ hành vi nào ở đây là đúng với MySign thật cho tới khi có báo cáo chạy thật.

[Bắt đầu với MySign](./mysign/getting-started){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[Xem trên GitHub](https://github.com/ChuKhaLi/ViettelConnect){: .btn .fs-5 .mb-4 .mb-md-0 }

---

## Phạm vi

- Chỉ ký **mã băm SHA-256**; SDK không đọc, không dựng, không gắn chữ ký vào tài liệu và không kèm thư viện PDF.
- Ba kiểu nhận kết quả: đồng bộ, hỏi trạng thái định kỳ (mặc định), callback.
- Thông tin đăng nhập theo từng người ký và từng đơn vị (lab), không giả định một cặp `client_id`/`client_secret` cho cả tiến trình.

## Mục lục

- [Kiến trúc](./architecture)
- [MySign](./mysign/): bắt đầu, các kiểu ký, callback, lỗi, hướng dẫn ký PDF.
