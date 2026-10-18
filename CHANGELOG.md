# Changelog

## 0.2.0 (2026-10-20)

First NuGet release.

### Added

- Imperva (reese84): `await session.ImpervaAsync(scriptUrl, script)` returns `ImpervaResponse`. Example: `examples/<client>/imperva.cs`.

### Changed

- Akamai sensor: the pass is `_abck` containing `~0~` or the exact 17-byte `{"success": true}` reply (docs and examples).
- Kasada: each protected request sends the `x-kpsdk-h` from `mfc` and a fresh `x-kpsdk-cd` (docs and examples).
- TLS: use the newest Chrome profile the library has, and keep the User-Agent and `sec-ch-ua` on the latest Chrome (examples use Chrome 155).
- Cloudflare response maps are sent with keys in UTF-8 byte order, as the other SDKs do.
