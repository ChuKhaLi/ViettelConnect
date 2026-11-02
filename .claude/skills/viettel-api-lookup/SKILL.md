---
name: viettel-api-lookup
description: Use when you need a Viettel MySign wire detail — an endpoint's request/response fields, a status or error code, the async/callback contract, OIDs, or token lifetime — before writing or reviewing ViettelConnect.MySign code. Reads the local gitignored Viettel doc instead of guessing.
---

# Viettel MySign API lookup

Source of truth: `.local/viettel-docs/mysign-api-sdk-v1.8.txt` (UTF-8 text of Viettel's
"Tài liệu tích hợp dịch vụ MySign API+SDK v1.8"). It is **gitignored and copyrighted** — quote only
the few lines you need, never copy it into tracked files, commits, PRs, or tests.

## If the text file is missing

Download `tai_lieu_tich_hop_dich_vu_mysign_api+sdk_v1.8.pdf` from viettel-ca.vn → Tải về into
`.local/viettel-docs/mysign-api-sdk-v1.8.pdf` (ask the user if you cannot), then:

```
pdftotext -enc UTF-8 -layout .local/viettel-docs/mysign-api-sdk-v1.8.pdf .local/viettel-docs/mysign-api-sdk-v1.8.txt
```

The error-code table's columns get scrambled by `-layout`; for that table re-extract with
`pdftotext -enc UTF-8 -raw` to a separate file (e.g. `mysign-api-sdk-v1.8.raw.txt`).

## Where things are (Phụ lục 1 is the only in-scope appendix)

Grep the endpoint URL or heading — line numbers below are approximate for the current extraction.

| Topic | Grep for | ~Line |
|---|---|---|
| Login (client + user) | `ras/v1/login` | 387 |
| Certificates | `certificates/info` | 487 (sync), 1089 (async) |
| Sign hash, sync (`async` 0) | `1.2.3. SignHash` | 750 |
| Sign hash, async poll | `2. Đặc tả giao tiếp ký bất đồng bộ`, `signHa` | 978–1748 |
| Request status | `service/request` | 1624 |
| Callback (`async` = 3) | `async = 3` | 1749–2028 |
| Common error codes | `4. Danh sách mã lỗi chung` | 2029–2365 |

Phụ lục 2–5 (device enrolment, OTP, pending requests, mobile SDKs) are **out of scope** — they are
for partners writing their own mobile app. Don't implement from them.

## Known traps (already decided — don't relitigate)

- Poll `async` value: doc says `1` in one place, `2` in another. SDK default `2`, hidden option
  `Advanced.PollingAsyncCode` ∈ {1,2}. Unverified until a real account exists.
- `document_name` / `description`: base64(UTF-8) < 100 chars, source restricted to
  `[A-Za-z0-9_\- ]`; SDK strips diacritics (`đ/Đ` → `d/D`).
- `expires_in` comes as a string of seconds; numbers/strings are loose — use the loose converter.
- Callback signatures may contain `\r\n` inside base64 — strip before decoding.
- `certificates/info` returns a JSON **array**.

## Reporting back

State the section + approximate line you read, the exact field names/casing, and flag anything the
doc contradicts itself on. If the doc is silent, say so — do not invent a field; record it as a
question for Viettel (spec §7).
