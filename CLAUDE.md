# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# ViettelConnect

**ViettelConnect** is a community .NET SDK for Viettel-CA digital-signature services. The first (and
currently only) package is **`ViettelConnect.MySign`** — remote signing via Viettel **MySign**
(the signer approves each request in the MySign mobile app; the SDK signs **SHA256 hashes only**).

- Sister project and **the skeleton this repo copies**: MisaConnect (github.com/ChuKhaLi/MisaConnect),
  package `MisaConnect.ESign`. Same author, same layering, same tooling. When unsure how to shape
  something, look at how `MisaConnect.ESign` does it first.
- Design (binding): [docs/superpowers/specs/2026-11-02-viettelconnect-mysign-design.md](docs/superpowers/specs/2026-11-02-viettelconnect-mysign-design.md)
- Plans: `docs/superpowers/plans/`.
- First consumer: ELIMS (separate private repo) — its `ViettelCaProvider` (code `"VT"`) will use this
  SDK. ELIMS integration is a separate project; do not shape this SDK around ELIMS internals.

## Viettel API reference

- Source of truth: Viettel "Tài liệu tích hợp dịch vụ MySign API+SDK v1.8", public at
  viettel-ca.vn → Tải về. A local copy (PDF + extracted UTF-8 text) lives in
  `.local/viettel-docs/` — **gitignored, never commit it** (Viettel-CA copyright). Re-extract with
  `pdftotext -enc UTF-8 -layout`.
- Base URL `https://remotesigning.viettel.vn/vtss/service`. Endpoints in scope: `ras/v1/login`,
  `certificates/info`, `signHash`, `requests/status`, and the partner-hosted callback pair (async = 3).
- Known doc traps: async-poll value is `1` in one section and `2` in another (we default to `2`,
  hidden option, unverified); `document_name` must be base64 < 100 chars of `[A-Za-z0-9_\- ]`
  only (SDK strips Vietnamese diacritics).
- **No real Viettel account yet** — everything is verified against the in-repo fake server.
  Do not claim anything works against real MySign until it has been run against it.

## Hard rules

- **No PDF library, no AGPL/GPL dependency.** Dependencies limited to `Microsoft.Extensions.*` +
  `System.Text.Json` (as MisaConnect). License is MIT.
- **Never log or surface** `client_secret`, access tokens, signatures, or signer identifiers in plain
  text. Raw Viettel error bodies only behind an explicit diagnostic opt-in flag.
- Public API must not leak wire details (`async` codes, OIDs, base64 document names).
- Credentials are per signer (`user_id`/`profile_id`) **and** per tenant (`client_id`/`client_secret`
  can differ per lab) — never assume one process-wide client pair.
- **Not published.** No nuget.org tag/publish until real signing against Viettel works (spec §8
  milestone 2). Public API is not frozen before that.
- Identifiers (code, files, config keys, branches) in **English**. Vietnamese only for docs/comments
  and user-facing messages.

## Workflow

- Every change goes through a branch + PR; never push straight to `main`.
- `TreatWarningsAsErrors=true`; run `dotnet format` before committing.
- Unit tests: no network, fast. Integration tests: in-repo MySign fake; real-account tests must
  skip cleanly when credentials env vars are absent.

## Build & test (once scaffolded)

```
dotnet build ViettelConnect.slnx
dotnet test tests/ViettelConnect.MySign.UnitTests
dotnet test tests/ViettelConnect.MySign.IntegrationTests
dotnet format ViettelConnect.slnx
```
