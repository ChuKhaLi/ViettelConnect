---
name: port-from-misaconnect
description: Use when a ViettelConnect plan task says "Chép từ" / copy from MisaConnect, or when creating any file that has a MisaConnect.ESign counterpart (Directory.Build.props, CI workflows, options, token cache, HTTP handlers, log scrubber, fake server, DI, facade, LayeringTests).
---

# Port a file from MisaConnect

The sister repo lives at `../MisaConnect` (github.com/ChuKhaLi/MisaConnect). ViettelConnect.MySign
copies the `MisaConnect.ESign` shape; copying beats re-inventing, but MISA-only behaviour must not
leak in.

## Steps

1. Open the source: `../MisaConnect/src/MisaConnect.ESign.<Layer>/...` (tests:
   `../MisaConnect/tests/MisaConnect.ESign.{UnitTests,IntegrationTests}/...`, fake:
   `IntegrationTests/EsignFake/`). If `../MisaConnect` is absent, ask the user — don't reconstruct
   from memory.
2. Copy, then rename:
   - namespace `MisaConnect.ESign.*` → `ViettelConnect.MySign.*`
   - `Misa`/`MisaESign` type prefixes → `MySign` (facade: `IViettelMySignClient`)
   - config section `Misa:ESign` → `Viettel:MySign`; env vars `MISACONNECT_ESIGN_*` → `VIETTEL_MYSIGN_*`
   - package metadata/URLs → `ViettelConnect.MySign`, `https://github.com/ChuKhaLi/ViettelConnect`
3. Strip MISA-only parts: OTP sign-in/resend, PDF/Word/Excel/XML hash + attach, relink,
   `AuthUnderWebdev`, ESRM routing, `ClientHeadersHandler` (unless Viettel needs a header),
   unused packages (`HealthChecks`, `UserSecrets`) from `Directory.Packages.props`.
4. Re-check wire details against Viettel, not MISA — use the `viettel-api-lookup` skill. MISA field
   casing/envelopes must not survive the port.
5. Keep the hard rules from `CLAUDE.md`: no new dependencies beyond `Microsoft.Extensions.*` +
   `System.Text.Json`; `ToString`/logs never show secrets, tokens, signatures; Infrastructure stays
   `internal`.
6. Verify no leftovers:

   ```
   git grep -niE "misa|esign|otp|webdev|esrm" -- src tests samples
   git grep -nE "F:\\\\|C:\\\\Users"
   ```

   Hits must be intentional (e.g. a comment crediting MisaConnect). Then `dotnet build` (0 warnings)
   and the task's tests.
