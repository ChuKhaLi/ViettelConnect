---
title: MySign
layout: default
nav_order: 3
has_children: true
permalink: /mysign/
---

# ViettelConnect.MySign

Facade `IViettelMySignClient`: liệt kê chứng thư, ký mã băm (đồng bộ / hỏi trạng thái), bắt đầu ký có callback và xử lý hai endpoint callback.

> **Chưa phát hành, chưa kiểm chứng với tài khoản Viettel thật.**

## Hướng dẫn

- [Bắt đầu](./getting-started): cấu hình, DI, ký mã băm đầu tiên.
- [Các kiểu ký](./signing-modes): đồng bộ, hỏi trạng thái, callback.
- [Callback](./callback): hai endpoint đối tác và cách nối vào ASP.NET Core.
- [Lỗi](./errors): nhóm lỗi và cách xử lý.
- [Hướng dẫn ký PDF](./pdf-signing-guidance): ứng dụng tự dựng chữ ký PDF với chữ ký thô từ SDK.
