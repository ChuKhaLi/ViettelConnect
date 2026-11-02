# ViettelConnect.MySign — thiết kế bản đầu (ký số từ xa)

Ngày: 2026-11-02 · Trạng thái: chờ duyệt

## 1. Mục đích và lý do

Một phòng thí nghiệm dùng ELIMS (tự host) cần ký biên bản bằng chứng thư số **Viettel-CA**.
Viettel có hai hình thức: ký từ xa **MySign** (người ký xác nhận trên app điện thoại) và
**USB token**. Bản này chỉ làm **ký từ xa MySign**.

Thay vì viết thẳng vào ELIMS, chủ dự án muốn một **SDK .NET mã nguồn mở, độc lập**:

- dùng lại được cho các dự án khác của chủ dự án, không chỉ ELIMS;
- công khai cho cộng đồng giống **MisaConnect** (github.com/ChuKhaLi/MisaConnect, gói
  `MisaConnect.ESign` trên nuget.org) — cùng tác giả, cùng tinh thần, cùng chất lượng.

Người dùng đầu tiên: ELIMS (`ViettelCaProvider`, mã `"VT"`, hiện là placeholder) — việc tích
hợp vào ELIMS là **dự án thứ hai**, sẽ có thiết kế riêng sau khi SDK xong.

## 2. Mục tiêu

1. Repo GitHub **riêng** `ViettelConnect`, giấy phép **MIT**, gói NuGet **`ViettelConnect.MySign`**,
   phát hành lên nuget.org bằng GitHub Actions khi đẩy tag (như MisaConnect).
2. Bao trọn luồng ký từ xa của tài liệu Viettel **"Tài liệu tích hợp dịch vụ MySign API+SDK v1.8"**
   (tải ở viettel-ca.vn → Tải về → `tai_lieu_tich_hop_dich_vu_mysign_api+sdk_v1.8.pdf`):
   - Đăng nhập lấy token: `POST {base}/ras/v1/login` với `client_id`, `client_secret`,
     `profile_id`, `user_id` → `access_token` (Bearer, `expires_in` 3600). Không mật khẩu, không OTP.
   - Lấy chứng thư: `POST {base}/certificates/info` → danh sách chứng thư đang hoạt động
     (`credential_id`, chuỗi chứng thư, hiệu lực, thông tin chủ thể).
   - Ký mã băm: `POST {base}/signHash` — `credentialID`, `numSignatures`, `documents[]`
     (`document_id`, `document_name`), `hash[]` (SHA256 base64), `hashAlgo`
     `2.16.840.1.101.3.4.2.1`, `signAlgo` `1.2.840.113549.1.1.1`, `async`.
   - Hỏi trạng thái: `POST {base}/requests/status` với `transactionId` → `status`
     (`1` thành công, `4000` chờ xác nhận, `6000` đã xác nhận đang ký, `4001` hết giờ,
     `4002` người dùng từ chối) + `signatures[]`.
   - Base URL mặc định `https://remotesigning.viettel.vn/vtss/service`, cấu hình được.
3. **Ba kiểu nhận kết quả**, đều hỗ trợ:
   - đồng bộ (`async = 0`): `signHash` trả chữ ký sau khi người dùng xác nhận;
   - bất đồng bộ + hỏi trạng thái: SDK tự poll có giới hạn (interval + tổng timeout), hết giờ
     ném ngoại lệ timeout riêng — giống `PollSignStatus` của MisaConnect;
   - bất đồng bộ + callback (`async = 3`, Phụ lục 2 §3 của tài liệu): Viettel gọi về **hai API do
     bên tích hợp dựng** — (a) API cấp token cho Viettel (`grant_type=client_credentials`, client
     do bên tích hợp cấp, trả `status_code "00"` + `access_token`), (b) API nhận kết quả
     (`meta_data` gồm `transaction_id`/`status`, `data.results[]` gồm `document_id`/`signature`).
     SDK cung cấp phần xử lý (xác thực client gọi tới, cấp/kiểm token, phân tích payload, trả đúng
     định dạng phản hồi Viettel chờ), ứng dụng chỉ việc gắn vào route của mình.
4. **Ký nhiều tài liệu trong một lần xác nhận**: một lệnh `signHash` mang nhiều mã băm; API công
   khai nhận danh sách tài liệu và trả chữ ký theo từng tài liệu.
5. **Thông tin đăng nhập theo từng người ký**: `user_id`/`profile_id` thuộc người ký;
   `client_id`/`client_secret` thuộc ứng dụng nhưng **có thể khác nhau theo đơn vị** (mỗi lab tự
   host có thể có cặp riêng). SDK lấy cả hai qua một cổng mà ứng dụng tự cài (tương tự
   `IMisaCredentialsAccessor` + chế độ Dynamic của MisaConnect), có sẵn bản cài đặt đọc từ cấu hình
   cho trường hợp đơn giản. Token cache theo khoá (client + user).
6. **Tự xử lý ràng buộc tên tài liệu**: `document_name` phải là base64 (UTF-8) dài < 100 ký tự và
   bản gốc chỉ chứa `a-z A-Z 0-9 _ - (dấu cách)`. SDK tự bỏ dấu tiếng Việt (đ→d), thay ký tự cấm,
   cắt ngắn cho vừa giới hạn. Ứng dụng truyền tên tự nhiên.
7. **Lỗi có phân loại**: ánh xạ bảng mã lỗi trong tài liệu (vd `58071` tham số sai, `59033` sai
   user/client, `58093` thiếu SAD, `57091` chứng thư thiết bị hết hạn…) sang nhóm lỗi dễ xử lý
   (xác thực, tham số, người dùng từ chối, hết giờ, lỗi tạm thời, khác), giữ mã gốc + mô tả gốc.
   Rút kinh nghiệm MisaConnect: **có cách đọc được body lỗi thô** khi bật cờ chẩn đoán (MisaConnect
   từng phải chặn HttpClient mới đọc được lý do 400).
8. **Chừa chỗ cho ký tự động** (xem §3): cách SDK "lấy quyền ký" cho một yêu cầu đặt sau một cổng
   riêng, để thêm kiểu "phiên lưu sẵn / SAD" sau này mà không đổi API công khai.
9. Test: unit test + integration test chạy với **máy chủ MySign giả** (như `EsignFake` của
   MisaConnect) vì chưa có tài khoản thật; workflow integration test thật chạy tay khi có tài khoản.
10. Tài liệu: README + trang docs (GitHub Pages như MisaConnect), CHANGELOG, mẫu `samples/`
    (console + API có endpoint callback).

## 3. Không làm ở bản này

- **Ký tự động / lưu phiên** — làm sau. Tài liệu riêng của Viettel ("API ký số lưu phiên tự động
  ký ver 1.1", mục "Tài liệu tích hợp ký tự động cho lĩnh vực Y tế") hiện **404 trên trang Viettel**;
  chủ dự án sẽ xin lại khi liên hệ Viettel. Chỉ cần chừa chỗ (mục tiêu 8).
- **USB token** — dự án riêng, cần ký hai pha phía trình duyệt/plugin Viettel.
- Các luồng chỉ dành cho bên tự viết app mobile: đăng ký thiết bị, xác thực user + OTP,
  lấy yêu cầu chờ, uỷ quyền ký trong app, renew token bằng refresh_token của luồng thiết bị.
  Người ký dùng app MySign của Viettel.
- Luồng đăng ký tài khoản MySign (tài liệu ngân hàng/chứng khoán).
- **Xử lý PDF**: SDK **không** ký PDF, không dựng PAdES, không phụ thuộc thư viện PDF nào.
  Lý do: MySign chỉ ký mã băm (khác MISA có `documents/hash` + attach phía server), và thư viện PDF
  phổ biến (iText) là AGPL, kéo vào sẽ phá giấy phép MIT. Ứng dụng tự tính mã băm và tự gắn chữ ký
  (ELIMS dùng iText `PdfSignatureStamper` như hiện nay). Một gói bổ sung lắp PDF có thể làm sau.
- Gộp vào MisaConnect hoặc tách gói lõi dùng chung — đã cân nhắc và bác (§5).

## 4. Phản mục tiêu (dù chạy được vẫn coi là hỏng)

- Kéo vào phụ thuộc giấy phép không tương thích MIT, hoặc phụ thuộc nặng (Polly/Refit…) khi
  MisaConnect đã chứng minh chỉ cần `Microsoft.Extensions.*` + `System.Text.Json`.
- Ghi log hoặc trả lỗi để lộ `client_secret`, access token, hay chữ ký.
- API công khai buộc ứng dụng biết chi tiết wire của Viettel (`async = 3`, OID thuật toán, base64
  tên tài liệu…).
- Thiết kế mà muốn thêm ký tự động sau này phải phá vỡ API công khai.
- Chỉ chạy được với một cặp `client_id`/`client_secret` cố định cho cả tiến trình.

## 5. Ràng buộc và quyết định đã chốt (của chủ dự án)

- Làm ký từ xa trước; USB token sau.
- SDK **riêng, công khai**, repo riêng tên `ViettelConnect` (không gộp MisaConnect: hai họ gói ở
  MisaConnect vốn không dùng chung code nào, tên "MisaConnect" gây hiểu lầm cho gói Viettel,
  release đang chung một tag).
- Bản đầu **chỉ ký mã băm và trả chữ ký** (kèm chứng thư để ứng dụng tự dựng chữ ký PDF).
- Hỗ trợ cả ký từng lần (bấm app) lẫn ký tự động; ký tự động làm sau.
- **Chép bộ khung `MisaConnect.ESign`** rồi bỏ phần riêng của MISA (OTP, hash/attach PDF/Word/
  Excel/XML, relink) và thay bằng API MySign: 4 lớp Domain / Application / Infrastructure / Client,
  facade + DTO ở Client, use case ở Application, cổng (token cache, credentials accessor, certificate
  selector, signing-session store, wire client, delayer/clock), typed HttpClient với chuỗi handler
  (retry lỗi tạm thời → gắn Bearer có single-flight refresh → header), call logger.
- Cùng nền tảng với MisaConnect: `net8.0`, Central Package Management (`Directory.Packages.props`),
  xUnit, workflow `ci.yml` / `integration.yml` / `release.yml`.
- Chưa có tài khoản tích hợp; chủ dự án tự liên hệ Viettel. **Chưa có hạn chót.**

### Quyết định của người viết thiết kế (Claude) — chủ dự án kiểm lại khi đọc

- **Giá trị `async` cho kiểu hỏi trạng thái**: tài liệu v1.8 tự mâu thuẫn (§1.2.3 ghi `1` là bất
  đồng bộ; §2.2.3 ghi `2` và request mẫu gửi `"async": 2`). Mặc định **`2`** (theo request mẫu),
  đặt thành **tuỳ chọn ẩn/nâng cao** trong cấu hình (không lộ ra API công khai — không vi phạm §4).
  Kiểu poll coi là **chưa kiểm chứng** cho tới khi Viettel xác nhận (đã thêm vào §7 câu 7).
- **Callback theo lab**: thiết kế sẵn cho trường hợp **mỗi lab đăng ký URL callback + cặp
  client callback riêng** (trường hợp tổng quát). Handler callback xác định "ai đang gọi" từ
  `client_id` Viettel gửi tới (hoặc khoá trong route) qua một cổng resolver; signing session lưu
  kèm khoá đơn vị. Bản cài mặc định là một cấu hình duy nhất cho cả ứng dụng. Lý do: thêm theo-lab
  về sau sẽ phá API callback của 1.0.

## 6. Để lại cho người làm

- Tên cụ thể của facade, phương thức, DTO, ngoại lệ, khoá cấu hình (giữ phong cách MisaConnect).
- Có tách `Begin*` (không chặn, cho callback) như MisaConnect hay gộp bằng tham số chế độ.
- Cách chọn chứng thư mặc định khi tài khoản có nhiều chứng thư (giữ cổng `ICertificateSelector`).
- Cách phát hành token cho Viettel trong luồng callback (lưu ở đâu, thời hạn) — miễn là ứng dụng
  thay được qua cổng.
- Bảng phân nhóm mã lỗi chi tiết — lấy từ danh sách mã lỗi trong tài liệu v1.8.
- Có cung cấp sẵn endpoint ASP.NET Core cho callback hay chỉ handler trung lập — tuỳ, nhưng lõi
  không được phụ thuộc ASP.NET Core.

## 7. Câu hỏi để hỏi Viettel khi xin tài khoản (chủ dự án mang theo)

1. Môi trường thử (UAT/sandbox) — base URL, `client_id`/`client_secret` thử, tài khoản `user_id` thử.
2. `client_id`/`client_secret` cấp theo **đơn vị dùng** (mỗi lab) hay theo **nhà phát triển phần mềm**?
3. `profile_id` lấy ở đâu, có cố định theo loại tài khoản không?
4. Thời gian chờ xác nhận tối đa trước khi `4001`.
5. Gửi lại tài liệu "ký tự động / lưu phiên" (link trên web đang 404) và điều kiện được bật.
6. Callback: có yêu cầu whitelist IP / chứng chỉ gì phía bên tích hợp; URL callback + client
   callback đăng ký theo từng đơn vị hay một lần cho cả phần mềm.
7. Giá trị `async` đúng cho kiểu bất đồng bộ hỏi trạng thái: `1` hay `2` (tài liệu ghi cả hai).

## 8. Thế nào là xong

Chủ dự án chốt: **không phát hành gì lên nuget.org cho tới khi ký thật được với Viettel.**

- **Mốc 1 — code xong (kế hoạch này dừng ở đây):** toàn bộ mục tiêu §2 có code + test, CI xanh
  với máy chủ giả, workflow release + trang docs viết sẵn nhưng **chưa gắn tag, chưa publish**.
  API công khai chưa đóng băng — được sửa thoải mái khi gặp hành vi thật của Viettel.
- **Mốc 2 — phát hành 1.0.0 (việc sau, khi có tài khoản):** ký thật được một mã băm qua cả ba kiểu
  nhận kết quả, chữ ký kiểm tra được bằng chứng thư lấy từ `certificates/info`; chỉnh code theo
  hành vi thật (vd giá trị `async`); rồi mới giữ tên gói, cấu hình `NUGET_API_KEY`, bật GitHub
  Pages và gắn tag `v1.0.0`.
