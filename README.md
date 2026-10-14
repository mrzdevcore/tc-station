<p align="center">
  <img src="src/Tropicast.Station.App/Assets/tropicast-512.png" alt="Tropicast logo" width="120" height="120">
</p>

<h1 align="center">Tropicast Station</h1>

<p align="center">
  Cross-platform desktop broadcaster: captures your station's audio and streams it live to Tropicast.
</p>

<p align="center">
  <a href="https://github.com/tropicast/tc-station/actions/workflows/ci.yml"><img src="https://github.com/tropicast/tc-station/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI"></a>
  <a href="https://github.com/tropicast/tc-station/actions/workflows/release.yml"><img src="https://github.com/tropicast/tc-station/actions/workflows/release.yml/badge.svg" alt="Release workflow"></a>
  <a href="https://github.com/tropicast/tc-station/releases/latest"><img src="https://img.shields.io/github/v/release/tropicast/tc-station?sort=semver" alt="Latest release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/tropicast/tc-station" alt="License: Apache-2.0"></a>
  <a href="global.json"><img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10"></a>
  <a href="docs/packaging.md"><img src="https://img.shields.io/badge/platforms-Windows%20%7C%20macOS%20%7C%20Linux-4B5563" alt="Windows, macOS, Linux"></a>
</p>

Built with [Avalonia](https://avaloniaui.net/) on .NET 10. Sign in with your
Tropicast account ([tc-dashboard](https://github.com/tropicast/tc-dashboard)),
pick a station and an audio source, and press **Go Live**: the app publishes
MP3 and Ogg Opus to the Tropicast streaming servers
([tc-streaming](https://github.com/tropicast/tc-streaming)), or to your own
Icecast server.

## Features

- Sign in with Tropicast (device code in the browser); no host, mount or password to type.
- Capture a microphone, mixer or interface input, or what the computer plays:
  Windows WASAPI loopback, Linux PulseAudio/PipeWire monitors, macOS ScreenCaptureKit system audio.
- MP3 and Ogg Opus from one capture, each stream reconnecting on its own with backoff.
- Peak and RMS meters, clipping and silence warnings.
- Tray icon with live status; quitting a live broadcast asks first.
- Manual Icecast connection profiles, passwords in the OS credential store.
- Credential-safe logs and a diagnostics export.
- Self-contained packages: Windows MSIX, Linux AppImage, macOS DMG (Apple Silicon and Intel).

## Install

Download the package for your system from the
[latest release](https://github.com/tropicast/tc-station/releases/latest):

- **Windows:** `Tropicast-Station-<version>-win-x64.msix`. While releases are
  signed with a self-signed certificate, trust it first, then install:
  [Installing a self-signed MSIX](docs/packaging.md#installing-a-self-signed-msix-windows)
  (`scripts/install-windows-msix.ps1`, `tropicast.cer` from the release).
- **macOS:** `Tropicast-Station-<version>-osx-arm64.dmg` (Apple Silicon) or
  `-osx-x64.dmg` (Intel). Grant microphone and screen/system audio recording when asked.
- **Linux:** `Tropicast-Station-<version>-x86_64.AppImage`. It needs `pactl`/`parec`
  and `secret-tool`: see [runtime prerequisites](docs/packaging.md#runtime-prerequisites-for-users).

Check downloads against `SHA256SUMS.txt` in the release.

## Build and run

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
(pinned by `global.json`). On Linux also `secret-tool` and `pactl`/`parec`; on
macOS the Xcode Command Line Tools. Details:
[docs/contributing.md](docs/contributing.md#prerequisites).

```bash
dotnet build
dotnet test
dotnet run --project src/Tropicast.Station.App               # --demo-audio: synthetic sources on any OS
bash scripts/build-ffmpeg.sh linux-x64 && dotnet build -c Release   # bundled FFmpeg, needed to broadcast
```

Debug builds enable Avalonia Developer Tools (F12). Windows FFmpeg builds, the
macOS app bundle, opt-in hardware and Icecast tests, and CI:
[docs/contributing.md](docs/contributing.md).

## Solution layout

| Project                                | Responsibility                                                                                              |
| -------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| `src/Tropicast.Station.App`            | Avalonia UI (views, view models), composition root (`AppHost`)                                              |
| `src/Tropicast.Station.Core`           | Domain model and shared services; no UI or platform dependencies                                            |
| `src/Tropicast.Station.Audio`          | Audio capture abstractions, PCM conversion                                                                  |
| `src/Tropicast.Station.Audio.Windows`  | WASAPI input and render-endpoint loopback (NAudio)                                                          |
| `src/Tropicast.Station.Audio.Linux`    | PulseAudio / pipewire-pulse sources and sink monitors                                                       |
| `src/Tropicast.Station.Audio.MacOS`    | Core Audio input and ScreenCaptureKit system audio (native bridge)                                          |
| `src/Tropicast.Station.Encoding`       | FFmpeg MP3/Opus encoders and credential-safe Icecast publishers                                             |
| `src/Tropicast.Station.Infrastructure` | Profiles, OS credential stores, Tropicast API client, Icecast source handshake                              |
| `tests/`                               | Unit, headless UI, adapter, encoding and end-to-end tests ([details](docs/contributing.md#solution-layout)) |

Dependencies point inward: `App` → `Audio` / `Encoding` / `Infrastructure` → `Core`.

## Documentation

| Topic                                                       | Covers                                                                  |
| ----------------------------------------------------------- | ----------------------------------------------------------------------- |
| [Broadcasting](docs/reference/broadcasting.md)              | Tropicast account, Go Live / Stop, reconnect, tray, connection profiles |
| [Audio capture and levels](docs/reference/audio.md)         | Sources per OS, preview, hardware qualification, meters and warnings    |
| [FFmpeg / Icecast publishing](docs/reference/publishing.md) | Encoder sessions, source handshake, bundled FFmpeg and its licenses     |
| [Logging and diagnostics](docs/reference/diagnostics.md)    | Log files, what is never logged, diagnostics export                     |
| [Packaging and releases](docs/packaging.md)                 | Packages, MSIX install, release workflow, signing                       |
| [Signing secrets](docs/signing-secrets.md)                  | Creating the Windows and macOS signing certificates                     |
| [Contributing](docs/contributing.md)                        | Prerequisites, build, conventions, CI, opt-in and end-to-end tests      |
| [Changelog](CHANGELOG.md)                                   | Changes per release                                                     |
| [Third-party notices](THIRD_PARTY_NOTICES)                  | FFmpeg, LAME, Opus and OpenSSL licenses                                 |

## Contributing

One issue, one branch (`feature/…`, `fix/…`, `docs/…`, `ci/…`) and one pull
request. CI must pass before review. Add user-facing changes to the
`Unreleased` section of [CHANGELOG.md](CHANGELOG.md). Conventions and tests:
[docs/contributing.md](docs/contributing.md).

## License

Copyright 2026 Tropicast. Licensed under the [Apache License, Version 2.0](LICENSE);
see [NOTICE](NOTICE). The license does not grant use of the Tropicast name or
logo (section 6).

## Responsible use

- The software is provided "AS IS", without warranties or conditions of any
  kind. The authors and Tropicast are not liable for any damages or claims
  arising from its use (Apache License 2.0, sections 7 and 8).
- You are responsible for what you broadcast or host with it: rights and
  royalties for music and other content, and the broadcasting, privacy and
  other laws that apply to you and your listeners.
- Do not use it to distribute content you have no right to distribute, or for
  any unlawful purpose.
