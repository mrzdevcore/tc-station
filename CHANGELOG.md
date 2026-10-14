# Changelog

All notable user-facing changes. Format: [Keep a Changelog](https://keepachangelog.com/);
versions follow [Semantic Versioning](https://semver.org/).

When releasing, rename `Unreleased` to the version (for example `## [0.2.0] - 2026-11-01`) and add a
new empty `Unreleased` section. The release workflow publishes the matching section above the
auto-generated list of merged pull requests.

## [Unreleased]

## [0.2.0] - 2026-10-14

First public release.

### Fixed
- Windows: loopback capture keeps publishing during silence, and FFmpeg no longer opens a console window.
- Windows: `install-windows-msix.ps1` and `new-windows-signing-cert.ps1` run under Constrained Language Mode.

### Added
- Scripts to create the self-signed MSIX signing certificate and to trust it on a customer machine.
- Ogg Opus publishing (48/64/96 kbps) alongside MP3 from one capture, each stream reconnecting on its own; the bundled FFmpeg now includes libopus.
- End-to-end test suite against the Tropicast Icecast container (go-live, restart recovery, wrong password, mount in use, unreachable host), run in CI.
- Packaging: Windows MSIX, Linux AppImage and macOS DMG (Apple Silicon and Intel) with a tag-driven release workflow.
- Credential-safe rolling diagnostics and a diagnostics export.
- Per-profile stream quality and public stream metadata.
- Automatic reconnect with exponential backoff.
- Audio level meters, clipping and silence warnings.
- Go Live / Stop broadcast workflow with tray status.
