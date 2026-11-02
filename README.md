# ViettelConnect

Community .NET SDK cho dịch vụ chữ ký số từ xa **Viettel MySign** (`ViettelConnect.MySign`): ký mã băm SHA-256, người ký duyệt từng yêu cầu trên ứng dụng MySign.

> **Chưa phát hành và chưa kiểm chứng với tài khoản Viettel thật.** Chưa có gói trên nuget.org; API công khai chưa ổn định. Dự án chưa có tài khoản MySign: mọi hành vi chỉ được kiểm tra với máy chủ MySign giả nằm trong repo (`tests/ViettelConnect.MySign.IntegrationTests/Fake`). Không có gì ở đây được coi là chạy đúng với MySign thật cho tới khi chạy thật xong.

Giấy phép: MIT. Dự án cộng đồng, không liên kết với Viettel-CA.

## Thao tác hỗ trợ

| Thao tác | API | Ghi chú |
| --- | --- | --- |
| Liệt kê chứng thư | `ListCertificatesAsync` | Kèm chuỗi chứng thư DER. |
| Ký mã băm, đồng bộ | `SignHashesAsync` với `SignMode = Synchronous` | Một lời gọi chờ người ký xác nhận. |
| Ký mã băm, hỏi trạng thái | `SignHashesAsync` với `SignMode = Polling` (mặc định) | Giá trị `async` chưa kiểm chứng (xem câu hỏi gửi Viettel). |
| Ký mã băm, callback | `BeginSignHashesAsync` + `HandleCallbackTokenRequestAsync` + `HandleCallbackResultAsync` | Ứng dụng host hai endpoint. |

Phạm vi: chỉ SHA-256 hash. SDK không dựng chữ ký PDF và không kèm thư viện PDF (xem [hướng dẫn ký PDF](docs/mysign/pdf-signing-guidance.md)). Thông tin đăng nhập theo từng người ký và từng đơn vị (lab).

## Dùng thử

Chưa có gói NuGet; tham chiếu project `src/ViettelConnect.MySign.Client`.

```csharp
services.AddViettelMySign(configuration);   // mục "Viettel:MySign"

var result = await client.SignHashesAsync(new SignHashesRequestDto(
    [new HashDocumentDto("doc-1", "Biên bản thí nghiệm", sha256)]));
```

Bí mật đọc từ user-secrets hoặc biến môi trường, không ghi vào file. Xem [bắt đầu](docs/mysign/getting-started.md), `samples/ViettelConnect.MySign.Sample.Console` (liệt kê chứng thư, ký mã băm một file) và `samples/ViettelConnect.MySign.Sample.Api` (hai endpoint callback).

> **Sample API chỉ để demo:** endpoint `POST /viettel/sign` của `Sample.Api` **không có xác thực** — ai gọi được nó đều đẩy được lời nhắc ký lên điện thoại người ký đã cấu hình. Ứng dụng thật phải bảo vệ endpoint khởi tạo ký (xác thực, kiểm tra người dùng đúng là người ký, chống lạm dụng). Sample trả 400 cho lỗi `Validation` (đầu vào của người gọi), 502 cho các lỗi MySign khác.

## Cấu trúc

```
src/
  ViettelConnect.MySign.Domain/          lỗi, kiểu giá trị
  ViettelConnect.MySign.Application/     use case + cổng
  ViettelConnect.MySign.Infrastructure/  wire client, HttpClient, options, DI
  ViettelConnect.MySign.Client/          facade + DTO công khai (gói ViettelConnect.MySign)
samples/                                 console và minimal API
tests/
  ViettelConnect.MySign.UnitTests/       không mạng
  ViettelConnect.MySign.IntegrationTests/ máy chủ MySign giả; RealAccount/ bỏ qua nếu thiếu env
docs/                                    trang tài liệu (just-the-docs)
```

## Build và test

```
dotnet build ViettelConnect.slnx
dotnet test ViettelConnect.slnx
dotnet format ViettelConnect.slnx --verify-no-changes
```

Test tài khoản thật (`RealAccount`) chỉ chạy khi có đủ `VIETTEL_MYSIGN_CLIENT_ID`, `VIETTEL_MYSIGN_CLIENT_SECRET`, `VIETTEL_MYSIGN_PROFILE_ID`, `VIETTEL_MYSIGN_USER_ID` (tuỳ chọn `VIETTEL_MYSIGN_BASE_URL`); người ký phải xác nhận trên điện thoại. Thiếu biến thì test hiện Skipped. Workflow `integration.yml` chỉ chạy tay; `release.yml` chưa dùng (không gắn tag).

## Câu hỏi gửi Viettel

Khi xin tài khoản tích hợp:

1. Môi trường thử (UAT/sandbox): base URL, `client_id`/`client_secret` thử, tài khoản `user_id` thử.
2. `client_id`/`client_secret` cấp theo đơn vị dùng (mỗi lab) hay theo nhà phát triển phần mềm?
3. `profile_id` lấy ở đâu, có cố định theo loại tài khoản không?
4. Thời gian chờ xác nhận tối đa trước khi trả mã `4001`.
5. Tài liệu "ký tự động / lưu phiên" (liên kết trên web đang 404) và điều kiện được bật.
6. Callback: có yêu cầu whitelist IP / chứng chỉ gì phía đối tác; URL callback và client callback đăng ký theo từng đơn vị hay một lần cho cả phần mềm.
7. Giá trị `async` đúng cho kiểu hỏi trạng thái: 1 hay 2 (tài liệu ghi cả hai mục).

Các điểm mở phát hiện trong lúc cài đặt:

8. Tên trường `SAD` trong request `signHash` không có trong các bảng tham số của tài liệu (chỉ suy ra từ các mã lỗi 58064, 58065, 58089 nói về SAD). Tên trường chính xác là gì, khi nào bắt buộc?
9. `document_id`: bảng tham số ghi kiểu String nhưng ví dụ mẫu lại là số. Viettel chấp nhận kiểu nào, trả về kiểu nào?
10. Endpoint callback: tài liệu ghi phương thức GET nhưng ví dụ có thân JSON; và ghi mã HTTP 400/500 cho lỗi token, trong khi SDK trả HTTP 200 kèm `status_code` khác `"00"`. Viettel mong đợi điều nào?
11. Viettel có gửi lại callback kết quả nếu phản hồi có `status_code` khác `"00"` không? (SDK trả `"02"` cho bản gửi trùng sau khi giao dịch đã hoàn tất.)
12. Lời gọi `signHash` đồng bộ chặn tối đa bao lâu? (Mặc định SDK đặt thời gian chờ HTTP = `Polling.TotalTimeout` + 1 phút.)
