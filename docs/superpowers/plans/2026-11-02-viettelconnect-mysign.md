# ViettelConnect.MySign (mốc 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** SDK .NET `ViettelConnect.MySign` ký mã băm SHA256 qua Viettel MySign (đồng bộ, hỏi trạng thái, callback), test xanh trên máy chủ MySign giả — chưa phát hành.

**Architecture:** Chép khung `MisaConnect.ESign` (repo MisaConnect, github.com/ChuKhaLi/MisaConnect): Domain → Application (use case + cổng) → Infrastructure (wire client, HttpClient handlers, mặc định của cổng, DI) → Client (facade + DTO công khai). Không có PDF; ứng dụng tự băm và tự gắn chữ ký.

**Tech Stack:** .NET SDK 10 (global.json), target `net8.0`, C# latest, Central Package Management, `Microsoft.Extensions.*` 8.x, `System.Text.Json`, xUnit 2.4.2, `Microsoft.AspNetCore.Mvc.Testing` + `WebApplication` cho máy chủ giả, `Microsoft.Extensions.TimeProvider.Testing`.

**Spec:** `docs/superpowers/specs/2026-11-02-viettelconnect-mysign-design.md` (đọc trước). Tài liệu Viettel: `.local/viettel-docs/mysign-api-sdk-v1.8.txt` (gitignored; nếu thiếu, tải `tai_lieu_tich_hop_dich_vu_mysign_api+sdk_v1.8.pdf` ở viettel-ca.vn → Tải về rồi `pdftotext -enc UTF-8 -layout`). Bảng mã lỗi bị lệch cột khi `-layout` — dùng `pdftotext -enc UTF-8 -raw` cho phần bảng mã lỗi.

**Nguồn để chép:** repo MisaConnect nằm cạnh repo này (`../MisaConnect`). Mỗi task ghi rõ file MisaConnect tương ứng; chép rồi đổi tên/cắt phần riêng của MISA (OTP, PDF/Word/Excel/XML hash/attach, relink, `AuthUnderWebdev`, ESRM routing).

## Global Constraints

- Repo `ViettelConnect`, gói `ViettelConnect.MySign` (PackageId trên project Client), giấy phép MIT, `PackageProjectUrl`/`RepositoryUrl` = `https://github.com/ChuKhaLi/ViettelConnect`.
- `TreatWarningsAsErrors=true`, `Nullable=enable`, `EnforceCodeStyleInBuild=true` (chép `Directory.Build.props` của MisaConnect, đổi metadata).
- Phụ thuộc runtime chỉ `Microsoft.Extensions.*` + `System.Text.Json`. Cấm thư viện PDF, cấm phụ thuộc AGPL/GPL, cấm Polly/Refit.
- Domain không phụ thuộc gì; Application chỉ phụ thuộc Domain + `Microsoft.Extensions.Logging.Abstractions`; Infrastructure `internal` trừ DI entry, options và kiểu cần cho DI.
- Base URL mặc định `https://remotesigning.viettel.vn/vtss/service`. Route: `ras/v1/login`, `certificates/info`, `signHash`, `requests/status`.
- `hashAlgo` = `2.16.840.1.101.3.4.2.1`, `signAlgo` = `1.2.840.113549.1.1.1`, `async`: `0` đồng bộ, **`2` hỏi trạng thái (mặc định, tuỳ chọn ẩn `Advanced.PollingAsyncCode`, chưa kiểm chứng)**, `3` callback.
- `document_name` và `description` gửi lên: base64(UTF-8) **< 100 ký tự**, bản gốc chỉ `[A-Za-z0-9_\- ]`.
- Không bao giờ log/ToString lộ `client_secret`, access token, chữ ký, token callback. Body lỗi thô của Viettel chỉ hiện khi `Errors.IncludeRawErrorMessage = true`.
- Không lộ chi tiết wire (mã `async`, OID, base64 tên) ra API công khai.
- Không gắn tag, không chạy `release.yml`, không publish (spec §8 mốc 1).
- Định danh tiếng Anh; tiếng Việt chỉ ở tài liệu/comment.
- Mọi thay đổi qua nhánh + PR; không push thẳng `main`.

## Review Focus

1. **Tên tài liệu tiếng Việt dài** (vd "Biên bản thí nghiệm máy biến áp 110kV — Trạm Đồng Hới số 2/2026") → phải ra chuỗi hợp lệ < 100 ký tự base64, không ném lỗi; `đ/Đ` → `d/D`. Test ở Task 3.
2. **Trạng thái lạ khi hỏi trạng thái** (mã không có trong bảng, vd `"4999"`, hoặc `status` rỗng) → coi là đang chờ, tiếp tục hỏi tới hết `TotalTimeout` rồi ném `MySignTimeoutException`; không treo vô hạn, không coi là thành công. Test ở Task 8.
3. **Hai người ký khác `client_id` (hai lab) chạy song song trong cùng tiến trình** → token cache không được dùng nhầm token của nhau. Test ở Task 6.
4. **Viettel trả số chữ ký khác số mã băm** (thiếu phần tử) → ném `MySignException` loại `ProtocolViolation`, không trả kết quả lệch thứ tự. Test ở Task 7.
5. **Callback gọi tới với token hết hạn / sai, hoặc `transaction_id` không biết** → trả `status_code` khác `"00"`, không ném lỗi 500, không lộ chi tiết. Test ở Task 9.

---

### Task 1: Khung solution + CI

**Files:**
- Create: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config`, `ViettelConnect.slnx`, `LICENSE` (MIT, "Copyright (c) 2026 ChuKhaLi"), `README.md` (stub: mục đích + "chưa phát hành"), `CHANGELOG.md` (`## [Unreleased]`), `icon.png` (để trống được — nếu thiếu thì bỏ `PackageIcon`)
- Create projects: `src/ViettelConnect.MySign.Domain`, `src/ViettelConnect.MySign.Application`, `src/ViettelConnect.MySign.Infrastructure`, `src/ViettelConnect.MySign.Client` (PackageId `ViettelConnect.MySign`, Version `0.1.0`, `IsPackable=true`; ba project kia `IsPackable=false` và được đóng gói kèm như MisaConnect làm), `tests/ViettelConnect.MySign.UnitTests`, `tests/ViettelConnect.MySign.IntegrationTests`
- Create: `.github/workflows/ci.yml` (chép MisaConnect, một bước unit test + một bước integration test fake), `.editorconfig` (chép)
- Test: `tests/ViettelConnect.MySign.UnitTests/Layering/LayeringTests.cs` (chép `MisaConnect.ESign.UnitTests/Layering`)

**Interfaces:** Produces: namespace gốc `ViettelConnect.MySign.{Domain|Application|Infrastructure|Client}`; `InternalsVisibleTo` từ Infrastructure/Application sang hai project test.

- [ ] **Step 1:** Tạo nhánh `feat/scaffold` từ `main`.
- [ ] **Step 2:** Chép + đổi tên các file gốc ở trên từ MisaConnect; `Directory.Packages.props` chỉ giữ gói đang dùng (bỏ `HealthChecks`, `UserSecrets` nếu không cần). `PackageTags`: `viettel;viettel-ca;mysign;remote-signing;digital-signature;vietnam;sdk`.
- [ ] **Step 3:** Viết `LayeringTests`: Domain không tham chiếu assembly nào ngoài BCL; Application chỉ tham chiếu Domain + `Microsoft.Extensions.Logging.Abstractions`; không assembly nào tham chiếu tên chứa `itext`, `Polly`, `Refit`, `PdfSharp`.
- [ ] **Step 4:** Run `dotnet build ViettelConnect.slnx && dotnet test ViettelConnect.slnx` → build 0 warning, LayeringTests PASS.
- [ ] **Step 5:** Commit `chore: scaffold ViettelConnect.MySign solution + CI`.

### Task 2: Domain — chứng thư, trạng thái ký, lỗi

**Files:**
- Create: `src/ViettelConnect.MySign.Domain/Certificates/Certificate.cs`, `Certificates/GeneralizedTime.cs`, `Signing/SignStatus.cs`, `Signing/SignStatusCodes.cs`, `Errors/MySignErrorCategory.cs`, `Errors/MySignException.cs`, `Errors/MySignTimeoutException.cs`, `Errors/MySignErrorCatalog.cs`
- Test: `tests/ViettelConnect.MySign.UnitTests/Domain/{GeneralizedTimeTests,SignStatusCodesTests,MySignErrorCatalogTests}.cs`

**Interfaces — Produces:**
```csharp
public sealed record Certificate(
    string CredentialId, string? SerialNumber, string? SubjectDn, string? IssuerDn,
    DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, bool KeyEnabled,
    IReadOnlyList<string> SignAlgorithmOids, IReadOnlyList<byte[]> Chain /* DER, user cert first */);
public static class GeneralizedTime { public static DateTimeOffset? TryParse(string? value); } // "20180709132216+0000" và "YYYYMMDDHHMMSSZ"
public enum SignStatus { Pending, Succeeded, Rejected, TimedOut, Failed }
public static class SignStatusCodes { public static SignStatus Classify(string? code); }
public enum MySignErrorCategory { Unknown, Authentication, Validation, NoActiveCertificate, SignRejected, SignTimeout, SignFailed, InsufficientQuota, CertificateInvalid, Transport, ProtocolViolation, CallbackValidation }
public class MySignException : Exception { Category; string? RawCode; string Detail; string CorrelationId; } // ctor như ESignException của MisaConnect
public sealed class MySignTimeoutException : MySignException { string? TransactionId; }
public static class MySignErrorCatalog { public static MySignErrorCategory Classify(string? rawCode); }
```

- [ ] **Step 1: Test** `SignStatusCodesTests` (theory): `"1"`→Succeeded; `"4000"`,`"4003"`,`"4007"`,`"6000"`→Pending; `"4001"`→TimedOut; `"4002"`→Rejected; `"4004"`,`"4006"`,`"4008"`,`"50000"`→Failed; `"4005"`→Failed; `"13003"`,`"13004"`→Failed; `"4999"`, `""`, `null`→Pending.
- [ ] **Step 2: Test** `GeneralizedTimeTests`: `"20180709132216+0000"` → `2018-07-09T13:22:16Z`; `"20190709132216Z"` → `2019-07-09T13:22:16Z`; `"garbage"` → null.
- [ ] **Step 3: Test** `MySignErrorCatalogTests`: `"59033"`→Authentication; `"86024"`→Authentication (client secret invalid); `"58058"`→Validation (numSignatures không khớp); `"58008"`→Authentication (User ID không tồn tại); `"invalid_request"`→Validation; `"zzz"`→Unknown.
- [ ] **Step 4:** Run tests → FAIL (chưa có kiểu).
- [ ] **Step 5:** Implement. `MySignErrorCatalog`: bảng tĩnh lấy từ "Danh sách mã lỗi chung" + bảng lỗi signHash của tài liệu (đọc bằng `-raw`); nhóm: lỗi client/secret/profile/user → Authentication; thiếu/sai tham số → Validation; hết giờ xác nhận mobile → SignTimeout; người dùng huỷ → SignRejected; chứng thư/khoá → CertificateInvalid; còn lại Unknown. Ghi nguồn (mục tài liệu) trong comment.
- [ ] **Step 6:** Run tests → PASS. Commit `feat(domain): certificate, sign status, error catalog`.

### Task 3: Mã hoá tên tài liệu hiển thị

**Files:**
- Create: `src/ViettelConnect.MySign.Application/Signing/DisplayLabelEncoder.cs`
- Test: `tests/ViettelConnect.MySign.UnitTests/Signing/DisplayLabelEncoderTests.cs`

**Interfaces — Produces:** `public static class DisplayLabelEncoder { public static string Encode(string? label, string fallback); }` — trả base64(UTF-8) hợp lệ; `label` rỗng/null/toàn ký tự cấm → dùng `fallback` (cũng qua cùng quy trình).

- [ ] **Step 1: Test:**
  - `Encode("Biên bản số 12", "doc")` decode ra `"Bien ban so 12"`.
  - `Encode("Đồng Hới", "doc")` decode ra `"Dong Hoi"`.
  - `Encode("A/B:C*D", "doc")` decode chỉ chứa `[A-Za-z0-9_\- ]` (ký tự cấm → dấu cách, gộp khoảng trắng liên tiếp, trim).
  - **Review Focus 1:** `Encode("Biên bản thí nghiệm máy biến áp 110kV — Trạm Đồng Hới số 2/2026 — lần đo lặp lại thứ ba", "doc")` → `result.Length < 100`, decode khớp regex `^[A-Za-z0-9_\- ]+$`, là tiền tố của bản đã bỏ dấu.
  - `Encode("###", "Document 7")` decode ra `"Document 7"`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement: `string.Normalize(FormD)` bỏ `NonSpacingMark`, map `đ→d`,`Đ→D`, thay ký tự ngoài tập bằng dấu cách, gộp/trim; cắt dần cho tới khi base64 < 100 (tức ≤ 72 byte gốc ASCII; cắt theo ký tự rồi trim).
- [ ] **Step 4:** Run → PASS. Commit `feat: display label encoder for MySign document names`.

### Task 4: Cổng thông tin đăng nhập, options, token cache

**Files:**
- Create Application/Abstractions: `MySignCredentials.cs`, `IMySignCredentialsAccessor.cs`, `ITokenCache.cs`, `AccessToken.cs`, `ICertificateSelector.cs`, `IDelayer.cs`, `ICorrelationIdAccessor.cs`, `ISignAuthorizationProvider.cs`
- Create Infrastructure: `Configuration/MySignOptions.cs`, `Configuration/MySignOptionsValidator.cs`, `Configuration/CredentialsMode.cs`, `Credentials/OptionsMySignCredentialsAccessor.cs`, `Caching/InMemoryTokenCache.cs`, `Certificates/FirstUsableCertificateSelector.cs`, `Signing/MobileConfirmationAuthorizationProvider.cs`, `Time/TaskDelayer.cs`
- Chép từ: MisaConnect `Abstractions/{MisaCredentials,IMisaCredentialsAccessor,ITokenCache,AccessToken,ICertificateSelector,IDelayer}`, `Infrastructure/{Configuration,Credentials,Caching,Certificates,Time}`
- Test: `tests/.../Configuration/MySignOptionsValidatorTests.cs`, `Abstractions/MySignCredentialsTests.cs`, `Certificates/FirstUsableCertificateSelectorTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record MySignCredentials(string ClientId, string ClientSecret, string ProfileId, string UserId)
{ public string CacheKey => $"{ClientId}|{ProfileId}|{UserId}"; /* ToString che ClientSecret */ }
public interface IMySignCredentialsAccessor { MySignCredentials Get(); }
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);
public interface ITokenCache { Task<AccessToken?> TryGetAsync(string key, CancellationToken ct); Task SetAsync(string key, AccessToken v, CancellationToken ct); Task RemoveAsync(string key, CancellationToken ct); }
public interface ICertificateSelector { Task<Certificate> SelectAsync(IReadOnlyList<Certificate> certificates, CancellationToken ct); }
// Chỗ chừa cho ký tự động (spec mục tiêu 8). Bản đầu trả null = người ký xác nhận trên app.
public interface ISignAuthorizationProvider { ValueTask<string?> GetSadAsync(MySignCredentials who, Certificate cert, CancellationToken ct); }
public sealed class MySignOptions {
  public const string SectionName = "Viettel:MySign";
  public string BaseUrl { get; set; } = "https://remotesigning.viettel.vn/vtss/service";
  public string ClientId, ClientSecret, ProfileId, UserId { get; set; } = "";
  public CredentialsMode CredentialsMode { get; set; } = CredentialsMode.Static; // Static | Dynamic
  public SignMode SignMode { get; set; } = SignMode.Polling;                     // Synchronous | Polling
  public PollingOptions Polling { get; set; } = new(); // Interval 3s, TotalTimeout 5 phút
  public TransportRetryOptions TransportRetry { get; set; } = new(); // chép MisaConnect
  public ErrorOptions Errors { get; set; } = new();     // IncludeRawErrorMessage=false
  public CallbackOptions Callback { get; set; } = new(); // ClientId, ClientSecret, TokenLifetime 1h
  public AdvancedOptions Advanced { get; set; } = new(); // PollingAsyncCode = 2
}
```
`SignMode` đặt ở Domain hoặc Application (public, Client dùng lại). `FirstUsableCertificateSelector`: chứng thư đầu tiên có `KeyEnabled` và `ValidTo` null hoặc > now; không có → `MySignException(NoActiveCertificate)`.

- [ ] **Step 1: Test:** `MySignCredentials.ToString()` không chứa giá trị secret; validator: Static thiếu `UserId` → fail; Dynamic không cần ClientId/UserId; `BaseUrl` không phải https → fail; `Advanced.PollingAsyncCode` ∉ {1,2} → fail; selector bỏ qua chứng thư `KeyEnabled=false` và chứng thư hết hạn.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat: credentials, options, token cache, certificate selector ports`.

### Task 5: Wire client + máy chủ MySign giả

**Files:**
- Create Infrastructure/Wire: `LoginDtos.cs`, `CertificateInfoDtos.cs`, `SignHashDtos.cs`, `RequestStatusDtos.cs`, `ErrorDto.cs`, `MySignJsonOptions.cs`, `MySignRoutes.cs`, `MySignWireClient.cs`, `Mapping/CertificateMapper.cs`, `Mapping/ErrorMapper.cs`
- Create Application/Abstractions: `IMySignWireClient.cs`, `SignHashWireRequest.cs`, `SignHashWireResponse.cs`, `StatusSnapshot.cs`
- Create: `tests/ViettelConnect.MySign.IntegrationTests/Fake/FakeMySignServer.cs` (chép khung `EsignFake/FakeMisaESignServer.cs`: `WebApplication` trên cổng ngẫu nhiên, ghi lại request đã nhận, kịch bản cấu hình được: chứng thư trả về, chuỗi status lần lượt cho mỗi `transactionId`, lỗi theo endpoint, đếm số lần login)
- Test: `tests/.../IntegrationTests/Wire/MySignWireClientTests.cs`

**Interfaces — Produces:**
```csharp
internal interface IMySignWireClient {
  Task<AccessToken> LoginAsync(MySignCredentials c, CancellationToken ct);
  Task<IReadOnlyList<Certificate>> GetCertificatesAsync(MySignCredentials c, CancellationToken ct);
  Task<SignHashWireResponse> SignHashAsync(MySignCredentials c, SignHashWireRequest r, CancellationToken ct);
  Task<StatusSnapshot> GetStatusAsync(MySignCredentials c, string transactionId, CancellationToken ct);
}
internal sealed record SignHashWireRequest(string CredentialId, IReadOnlyList<(string DocumentId, string EncodedName, byte[] Sha256)> Documents, string? EncodedDescription, int AsyncCode, string? Sad);
internal sealed record SignHashWireResponse(string? TransactionId, IReadOnlyList<byte[]>? Signatures);
internal sealed record StatusSnapshot(string? RawStatus, SignStatus Status, IReadOnlyList<byte[]> Signatures, string? ErrorCode, string? ErrorDescription);
```
Wire fields theo tài liệu: login body `client_id, client_secret, profile_id, user_id` → `access_token, token_type, expires_in` (chuỗi giây); certificates/info body thêm `certificates:"chain", certInfo:true, authInfo:true`, phản hồi là **mảng** (`credential_id`, `key.status`, `key.algo[]`, `cert.certificates[]`, `cert.SerialNumber`, `cert.subjectDN`, `cert.issuerDN`, `cert.validFrom`, `cert.validTo`); signHash body `credentialID, client_id, client_secret, numSignatures, description, documents[{document_id, document_name}], hash[], hashAlgo, signAlgo, async` (+ `SAD` khi khác null); `requests/status` body `transactionId`. Lỗi: `{error, error_description}` → `MySignException` theo `MySignErrorCatalog`; dùng converter lỏng cho số/chuỗi (chép `LooseStringConverter`).

- [ ] **Step 1: Test** (với fake): login gửi đúng 4 field và đọc `expires_in:"3600"` thành `ExpiresAtUtc ≈ now+3600s`; certificates map đủ field, chuỗi DER đúng thứ tự; signHash gửi `numSignatures == hash.Length == documents.Length`, `hashAlgo`/`signAlgo` đúng OID, `async` đúng giá trị truyền vào, không có field `SAD` khi null; fake trả 401 `{"error":"59033"}` → `MySignException` Category Authentication, `RawCode "59033"`, `Detail` **không** chứa body thô khi `IncludeRawErrorMessage=false` và chứa khi `true`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat: MySign wire client + fake server`.

### Task 6: HTTP pipeline — retry, Bearer, log

**Files:**
- Create Infrastructure/Http: `TransientFailureRetryHandler.cs`, `MySignAuthHandler.cs`, `SingleFlightRefresh.cs`; Logging: `MySignCallLogger.cs`, `MySignLogScrubber.cs`
- Chép từ: MisaConnect `Infrastructure/Http/*`, `Infrastructure/Logging/*` (bỏ `ClientHeadersHandler` nếu Viettel không cần header riêng)
- Test: `tests/.../IntegrationTests/Http/{TokenCacheIsolationTests,RefreshOn401Tests}.cs`, `tests/.../UnitTests/Logging/MySignLogScrubberTests.cs`

**Interfaces:** Consumes `ITokenCache`, `IMySignCredentialsAccessor`, `IMySignWireClient.LoginAsync`. Produces: mọi request trừ login mang `Authorization: Bearer`, token cache theo `MySignCredentials.CacheKey`; 401 → xoá cache, login lại đúng 1 lần, thử lại 1 lần.

- [ ] **Step 1: Test:**
  - **Review Focus 3:** hai credentials khác `ClientId` gọi `GetCertificatesAsync` song song 20 lần mỗi bên → fake thấy đúng 2 lần login, mỗi request certificates mang token đúng của client mình.
  - 401 ở certificates → 1 lần login lại, lần 2 thành công; 401 hai lần liên tiếp → `MySignException(Authentication)`.
  - Scrubber: chuỗi log chứa `client_secret":"abc"`, `access_token":"xyz"`, `Bearer xyz` → đều thành `<redacted>`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat: http pipeline with per-credential token cache`.

### Task 7: Use case ký đồng bộ + liệt kê chứng thư

**Files:**
- Create Application/UseCases: `ListCertificates.cs`, `SignHashes.cs`, `SignHashesRequest.cs`, `SignHashesResult.cs`
- Test: `tests/.../IntegrationTests/EndToEnd/SignSynchronousFakeServerTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record HashDocument(string DocumentId, string DisplayName, byte[] Sha256);
public sealed record SignHashesRequest(IReadOnlyList<HashDocument> Documents, string? Description, string? CredentialId);
public sealed record DocumentSignature(string DocumentId, byte[] Signature);
public sealed record SignHashesResult(Certificate Certificate, IReadOnlyList<DocumentSignature> Signatures, string? TransactionId);
internal sealed class SignHashes { Task<SignHashesResult> ExecuteAsync(SignHashesRequest r, CancellationToken ct); } // dùng options.SignMode
```
Trước khi gửi, gọi `ISignAuthorizationProvider.GetSadAsync` và đưa kết quả vào `SignHashWireRequest.Sad` (mặc định null). Validate: ≥1 tài liệu; `Sha256.Length == 32`; `DocumentId` không trùng; `CredentialId` null → `ICertificateSelector`; có `CredentialId` mà không nằm trong danh sách → `NoActiveCertificate`. Tên/description qua `DisplayLabelEncoder` (fallback = `DocumentId`).

- [ ] **Step 1: Test** (SignMode=Synchronous): 3 tài liệu → một lệnh signHash duy nhất, `async=0`, kết quả 3 chữ ký đúng thứ tự `DocumentId`; hash 31 byte → `MySignException(Validation)` trước khi gọi mạng; **Review Focus 4:** fake trả 2 chữ ký cho 3 hash → `MySignException(ProtocolViolation)`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement nhánh Synchronous. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat: synchronous hash signing`.

### Task 8: Ký bất đồng bộ — hỏi trạng thái

**Files:**
- Create Application/UseCases: `PollSignStatus.cs` (chép khung MisaConnect `PollSignStatus.cs`)
- Modify: `SignHashes.cs` (nhánh Polling)
- Test: `tests/.../IntegrationTests/EndToEnd/SignPollingFakeServerTests.cs` (dùng `FakeTimeProvider` hoặc `IDelayer` giả)

**Interfaces:** Consumes `IMySignWireClient.GetStatusAsync`, `SignStatusCodes`. Trạng thái `Failed` ánh xạ sang category theo mã gốc: `4005`→`InsufficientQuota`, `4006`/`13003`/`13004`→`CertificateInvalid`, còn lại→`SignFailed` (`RawCode` = mã status). Produces: SignMode=Polling gửi `async = options.Advanced.PollingAsyncCode` (mặc định 2), nhận `transactionId`, hỏi mỗi `Polling.Interval` tới `TotalTimeout`.

- [ ] **Step 1: Test:**
  - Chuỗi status `4000, 6000, 1` → thành công, đúng 3 lần hỏi, `TransactionId` có trong kết quả.
  - `4002` → `MySignException(SignRejected)`; `4001` → `MySignTimeoutException`; `4005` → `MySignException(InsufficientQuota)`; `13003` → `CertificateInvalid`.
  - **Review Focus 2:** status luôn `"4999"` → `MySignTimeoutException` sau `TotalTimeout`, số lần hỏi ≈ `TotalTimeout / Interval`.
  - `Advanced.PollingAsyncCode = 1` → fake nhận `async=1`.
  - Hủy `CancellationToken` giữa chừng → `OperationCanceledException`, không hỏi thêm.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat: async signing with bounded status polling`.

### Task 9: Ký bất đồng bộ — callback (theo lab)

**Files:**
- Create Application/Abstractions: `ICallbackClientResolver.cs`, `ICallbackTokenStore.cs`, `IPendingSignatureStore.cs`
- Create Application/UseCases: `BeginSignHashes.cs`, `HandleCallbackTokenRequest.cs`, `HandleCallbackResult.cs`, `CallbackResponse.cs`
- Create Infrastructure: `Callback/OptionsCallbackClientResolver.cs`, `Callback/InMemoryCallbackTokenStore.cs`, `Callback/InMemoryPendingSignatureStore.cs`, `Wire/CallbackDtos.cs`
- Test: `tests/.../IntegrationTests/EndToEnd/CallbackFakeServerTests.cs`, `tests/.../UnitTests/Callback/HandleCallbackTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record CallbackClient(string TenantKey, string ClientId, string ClientSecret);
public interface ICallbackClientResolver { ValueTask<CallbackClient?> FindAsync(string clientId, CancellationToken ct); } // mặc định: một client từ options.Callback, TenantKey "default"
public interface ICallbackTokenStore { ValueTask<string> IssueAsync(string tenantKey, TimeSpan lifetime, CancellationToken ct); ValueTask<string?> ValidateAsync(string token, CancellationToken ct); /* trả tenantKey */ }
public sealed record PendingSignature(string TransactionId, string TenantKey, Certificate Certificate, IReadOnlyList<string> DocumentIds, DateTimeOffset CreatedAt);
public interface IPendingSignatureStore { ValueTask SaveAsync(PendingSignature p, CancellationToken ct); ValueTask<PendingSignature?> FindAsync(string tenantKey, string transactionId, CancellationToken ct); ValueTask RemoveAsync(string tenantKey, string transactionId, CancellationToken ct); }
public sealed record BeginSignResult(string TransactionId, Certificate Certificate);
public sealed record CallbackResponse(int HttpStatus, string JsonBody); // JsonBody đúng định dạng Viettel chờ
public sealed record CallbackSignOutcome(string TenantKey, string TransactionId, SignStatus Status, IReadOnlyList<DocumentSignature> Signatures, string? RawStatus);
```
- `BeginSignHashes.ExecuteAsync(SignHashesRequest, string tenantKey, ct)` gửi `async=3`, lưu `PendingSignature`, trả `BeginSignResult`.
- `HandleCallbackTokenRequest.ExecuteAsync(string requestJson, ct)` → body `{grant_type:"client_credentials", client_id, client_secret}`; khớp resolver → `200 {"status_code":"00","message":"Thành công","data":{"access_token":…,"expires_in":"<ms>","consented_on":"<unix s>"}}`; sai → `200 {"status_code":"01","message":"invalid_client"}` (không lộ lý do chi tiết).
- `HandleCallbackResult.ExecuteAsync(string bearerToken, string requestJson, ct)` → `(CallbackResponse Response, CallbackSignOutcome? Outcome)`; body `meta_data{request_id, transaction_id, description, status}`, `data{action_timestamp, signature_timestamp, results[{document_id, signature}]}` (signature base64 có thể chứa `\r\n` → bỏ trước khi decode). Thành công → `{"status_code":"00","message":"Thành công"}`, xoá pending.
- Lõi không phụ thuộc ASP.NET Core; nhận/trả chuỗi JSON. Chấp nhận cả GET lẫn POST là việc của sample (Task 11).

- [ ] **Step 1: Test:** token request đúng client → `status_code "00"` + token dùng được ở result; hai `CallbackClient` (TenantKey `labA`, `labB`) — token của `labA` không dùng được để báo kết quả giao dịch của `labB` (`status_code` ≠ `"00"`, Outcome null); **Review Focus 5:** token hết hạn (FakeTimeProvider vượt lifetime) / token rác / `transaction_id` không có → `status_code` ≠ `"00"`, `HttpStatus 200`, không ném; `results` có signature chứa `\r\n` → decode đúng; status `"4002"` → Outcome `Rejected`, không có chữ ký; `ToString`/log không chứa token.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat: callback-based async signing with per-tenant callback clients`.

### Task 10: Client facade + DI + DTO công khai

**Files:**
- Create Client: `IViettelMySignClient.cs`, `ViettelMySignClient.cs`, `Dtos/{CertificateDto,HashDocumentDto,SignHashesRequestDto,SignHashesResultDto,DocumentSignatureDto,BeginSignResultDto,CallbackResponseDto,CallbackSignOutcomeDto}.cs`, `Mapping/*.cs`, `DependencyInjection/ServiceCollectionExtensions.cs`, `LibraryCorrelationIdAccessor.cs`
- Create Infrastructure: `DependencyInjection/ServiceCollectionExtensions.cs` (đăng ký typed `AddHttpClient<MySignWireClient>` + handlers + mặc định của mọi cổng bằng `TryAdd`)
- Chép từ: MisaConnect `Client/{IMisaESignClient,MisaESignClient,DependencyInjection}`, `Infrastructure/DependencyInjection`
- Test: `tests/.../IntegrationTests/EndToEnd/FacadeFakeServerTests.cs`, `tests/.../UnitTests/Client/PublicSurfaceTests.cs`

**Interfaces — Produces:**
```csharp
public interface IViettelMySignClient {
  Task<IReadOnlyList<CertificateDto>> ListCertificatesAsync(CancellationToken ct = default);
  Task<SignHashesResultDto> SignHashesAsync(SignHashesRequestDto request, CancellationToken ct = default);
  Task<BeginSignResultDto> BeginSignHashesAsync(SignHashesRequestDto request, string tenantKey = "default", CancellationToken ct = default);
  Task<CallbackResponseDto> HandleCallbackTokenRequestAsync(string requestJson, CancellationToken ct = default);
  Task<(CallbackResponseDto Response, CallbackSignOutcomeDto? Outcome)> HandleCallbackResultAsync(string? bearerToken, string requestJson, CancellationToken ct = default);
}
public static IServiceCollection AddViettelMySign(this IServiceCollection services, IConfiguration configuration); // bind "Viettel:MySign"
public static IServiceCollection AddViettelMySign(this IServiceCollection services, Action<MySignOptions> configure);
```
Dynamic mode: ứng dụng đăng ký `IMySignCredentialsAccessor` của mình (vd đọc AsyncLocal theo người ký) **trước hoặc sau** `AddViettelMySign` đều thắng mặc định (`TryAdd`).

- [ ] **Step 1: Test:** end-to-end qua DI với fake: `ListCertificatesAsync`, `SignHashesAsync` (Polling mặc định), `BeginSignHashesAsync` + hai handler callback; Dynamic mode với accessor tự viết đổi `UserId` giữa hai lần gọi → fake thấy hai `user_id` khác nhau. `PublicSurfaceTests`: không kiểu public nào có property/tham số tên chứa `Async`Code/`Oid`/`Base64` (reflection trên assembly Client); assembly Infrastructure chỉ public các kiểu trong allow-list (DI extensions, `MySignOptions` và options con, `CredentialsMode`).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run `dotnet test ViettelConnect.slnx` → toàn bộ PASS.
- [ ] **Step 5:** Commit `feat: public client facade + DI`.

### Task 11: Samples, tài liệu, workflows (không phát hành)

**Files:**
- Create: `samples/ViettelConnect.MySign.Sample.Console/` (đọc cấu hình từ user-secrets/env, liệt kê chứng thư, ký mã băm của một file, in chữ ký base64), `samples/ViettelConnect.MySign.Sample.Api/` (minimal API: `/viettel/callback/token` và `/viettel/callback/result` nhận GET hoặc POST, chuyển body sang facade)
- Create: `docs/index.md`, `docs/architecture.md`, `docs/mysign/{getting-started,signing-modes,callback,errors,pdf-signing-guidance}.md`, `docs/_config.yml` (chép MisaConnect)
- Modify: `README.md` (bảng thao tác hỗ trợ, cảnh báo "chưa kiểm chứng với tài khoản Viettel thật", mục câu hỏi gửi Viettel), `CHANGELOG.md` (`[Unreleased]`)
- Create: `.github/workflows/integration.yml` (chỉ `workflow_dispatch`, chạy integration test với secrets `VIETTEL_MYSIGN_*`), `.github/workflows/release.yml` (chép MisaConnect, đổi tên gói; **không gắn tag**)
- Create: `tests/.../IntegrationTests/RealAccount/RealAccountFactAttribute.cs` + `RealAccountSmokeTests.cs` (bỏ qua sạch khi thiếu env `VIETTEL_MYSIGN_CLIENT_ID|CLIENT_SECRET|PROFILE_ID|USER_ID`; khi có: liệt kê chứng thư, ký 1 mã băm Polling, kiểm chữ ký bằng `RSA.VerifyHash` với chứng thư đầu chuỗi)

`pdf-signing-guidance.md`: hướng dẫn ứng dụng dựng chữ ký PDF bằng thư viện của mình (lấy `Chain` làm chứng thư, băm phần SignedAttributes, ký qua SDK) — nêu rõ SDK không kèm thư viện PDF vì giấy phép.

- [ ] **Step 1:** Viết samples; `dotnet build ViettelConnect.slnx` → 0 warning.
- [ ] **Step 2:** Run `dotnet test tests/ViettelConnect.MySign.IntegrationTests --filter "FullyQualifiedName~RealAccount"` không có env → test hiển thị Skipped, không Failed.
- [ ] **Step 3:** Viết tài liệu + workflows. `dotnet format ViettelConnect.slnx --verify-no-changes` → exit 0.
- [ ] **Step 4:** Commit `docs: samples, docs site, workflows (unpublished)`.

### Task 12: Chốt mốc 1

- [ ] **Step 1:** Run `dotnet build ViettelConnect.slnx -c Release && dotnet test ViettelConnect.slnx -c Release` → 0 warning, 0 failed (RealAccount skipped).
- [ ] **Step 2:** `dotnet pack src/ViettelConnect.MySign.Client -c Release -o artifacts` → một `.nupkg` duy nhất; liệt kê nội dung bằng `unzip -l` → có 4 assembly, README, không có thư viện PDF. **Không push.**
- [ ] **Step 3:** Kiểm `git grep -nE "F:\\\\|C:\\\\Users"` → rỗng (không có đường dẫn máy cá nhân trong file commit).
- [ ] **Step 4:** Tạo remote GitHub `ChuKhaLi/ViettelConnect` **chỉ khi chủ dự án đồng ý**; mở PR từ nhánh làm việc vào `main`. Không gắn tag.
