---
title: Hướng dẫn ký PDF
layout: default
parent: MySign
nav_order: 5
---

# Hướng dẫn ký PDF

SDK **chỉ ký mã băm SHA-256** và trả chữ ký thô cùng chuỗi chứng thư. SDK **không kèm thư viện PDF**: các thư viện PDF phổ biến dùng giấy phép AGPL/GPL, không tương thích với ràng buộc MIT của dự án. Ứng dụng tự dựng chữ ký PDF bằng thư viện của mình (đã có giấy phép phù hợp).

## Quy trình

1. **Chuẩn bị chữ ký trong PDF** bằng thư viện của bạn: chừa vùng `/Contents` cho chữ ký CMS (PKCS#7 detached) và tính `ByteRange`.
2. **Lấy chứng thư**: gọi `ListCertificatesAsync()` (hoặc dùng `result.Certificate` sau khi ký). `Chain` là danh sách DER, chứng thư người ký đứng đầu; đưa cả chuỗi vào CMS (`certificates`) nếu muốn kiểm tra offline.
3. **Dựng CMS SignedData** với `digestAlgorithm = SHA-256` và `signatureAlgorithm = rsaEncryption`. Tính mã băm của nội dung theo `ByteRange`, đặt vào thuộc tính `messageDigest` của **SignedAttributes** (kèm `contentType`, `signingTime`, và `signingCertificateV2` nếu làm PAdES).
4. **Băm SignedAttributes**: mã hoá DER các SignedAttributes với thẻ `SET OF` (0x31) rồi tính SHA-256. Đây chính là mã băm cần ký.
5. **Ký qua SDK**:

   ```csharp
   var result = await client.SignHashesAsync(new SignHashesRequestDto(
       [new HashDocumentDto("pdf-1", "Tên tài liệu hiển thị cho người ký", signedAttributesHash)]));
   byte[] rawSignature = result.Signatures[0].Signature;
   ```

6. **Hoàn tất CMS**: đặt `rawSignature` vào `SignerInfo.signature`, gắn chuỗi chứng thư, nhúng CMS vào vùng `/Contents` của PDF.
7. **Kiểm tra** bằng chứng thư người ký: `RSA.VerifyHash(signedAttributesHash, rawSignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)`.

## Lưu ý

- `DocumentId` do ứng dụng đặt, dùng để ghép chữ ký khi ký nhiều tài liệu trong một lần xác nhận.
- Chữ ký là RSA PKCS#1 v1.5 trên SHA-256 (thuật toán ký `rsaEncryption`). Chưa kiểm chứng với MySign thật; test `RealAccountSmokeTests` sẽ kiểm điều này khi có tài khoản.
- Mỗi lần gọi ký là một yêu cầu xác nhận trên điện thoại người ký; gộp nhiều tài liệu vào một yêu cầu khi cần.
- Repo này không chứa mã phụ thuộc thư viện PDF nào.
