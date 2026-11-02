# Changelog

All notable changes to ViettelConnect will be documented here. This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

Chưa phát hành; chưa kiểm chứng với tài khoản Viettel thật (chỉ máy chủ MySign giả trong repo).

### Added

- `ViettelConnect.MySign`: liệt kê chứng thư, ký mã băm SHA-256 (đồng bộ, hỏi trạng thái, callback) qua `IViettelMySignClient` và `AddViettelMySign`.
- Samples: `ViettelConnect.MySign.Sample.Console` và `ViettelConnect.MySign.Sample.Api` (hai endpoint callback).
- Trang tài liệu `docs/` (just-the-docs), gồm hướng dẫn ký PDF bằng thư viện của ứng dụng.
- Test `RealAccount` (bỏ qua khi thiếu `VIETTEL_MYSIGN_*`), workflow `integration.yml` (chạy tay) và `release.yml` (chưa dùng).
