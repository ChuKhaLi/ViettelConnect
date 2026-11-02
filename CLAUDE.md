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

- Milestone-1 work follows the 12-task plan in `docs/superpowers/plans/`, executed with
  `superpowers:subagent-driven-development` (or `superpowers:executing-plans`). The plan's
  **Review Focus** list (long Vietnamese names, unknown poll statuses, two labs in parallel,
  signature-count mismatch, bad callback tokens) is the set of edge cases every reviewer checks.
- Project skills: `viettel-api-lookup` (find a field/code in the local Viettel doc),
  `port-from-misaconnect` (copy a MisaConnect file and strip MISA-only parts).
  Project agent: `mysign-rules-reviewer` (checks a diff against the hard rules above).

## Build & test

Repo is docs-only until plan Task 1 scaffolds the solution (.NET SDK 10 via `global.json`,
target `net8.0`, Central Package Management, xUnit).

```
dotnet build ViettelConnect.slnx
dotnet test tests/ViettelConnect.MySign.UnitTests
dotnet test tests/ViettelConnect.MySign.IntegrationTests
dotnet test tests/ViettelConnect.MySign.UnitTests --filter "FullyQualifiedName~DisplayLabelEncoderTests"   # one class
dotnet format ViettelConnect.slnx --verify-no-changes                                                     # CI gate
```

- Unit/integration split is by project, not by xUnit trait.
- Real-account tests use `[RealAccountFact]` and skip unless `VIETTEL_MYSIGN_CLIENT_ID`,
  `VIETTEL_MYSIGN_CLIENT_SECRET`, `VIETTEL_MYSIGN_PROFILE_ID`, `VIETTEL_MYSIGN_USER_ID` are set.
  Run them with `--filter "FullyQualifiedName~RealAccount"`.
- Fake server: `tests/ViettelConnect.MySign.IntegrationTests/Fake/FakeMySignServer.cs` (scripted
  certificates, per-transaction status sequences, per-endpoint errors, login counter).

## Architecture (MisaConnect.ESign layering)

`Domain` → `Application` → `Infrastructure` → `Client`, one NuGet package (`ViettelConnect.MySign`,
the Client project) that bundles the other three. `LayeringTests` enforce it:

- `Domain`: no dependencies. Certificates, `SignStatus`, `MySignException` + error catalog.
- `Application`: use cases + ports only (depends on Domain + `Logging.Abstractions`). Ports:
  `IMySignCredentialsAccessor`, `ITokenCache`, `ICertificateSelector`, `IDelayer`,
  `ISignAuthorizationProvider` (placeholder for future auto-sign/SAD — returns null = app approval),
  `IMySignWireClient`. New swappable collaborators go here, with an Infrastructure default.
- `Infrastructure`: `internal` except DI extensions and `MySignOptions` (+ child options,
  `CredentialsMode`). Wire DTOs, HttpClient handlers (transient retry, Bearer + single-flight
  refresh on 401), log scrubber, defaults registered with `TryAdd` so apps can override.
- `Client`: `IViettelMySignClient` facade + public DTOs; `services.AddViettelMySign(...)` binds
  section `Viettel:MySign`.

Signing flow: login (per `ClientId|ProfileId|UserId` token-cache key) → `certificates/info` →
pick certificate → `signHash` with `async` 0 (sync), 2 (poll `requests/status` until signed /
rejected / `TotalTimeout`; unknown statuses = still pending), or 3 (Viettel calls the partner's
callback endpoints; callback clients are per tenant via `tenantKey`). The callback core takes and
returns JSON strings — no ASP.NET Core dependency; hosting is the sample's job.
