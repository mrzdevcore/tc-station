# Contributing

One issue, one branch (`feature/…`, `fix/…`, `docs/…`, `ci/…`) and one pull request. CI must pass before review.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned by `global.json`)
- Windows 10+, Linux with X11 or Wayland (XWayland), or macOS 13+
- Linux credentials need **libsecret's `secret-tool`** (`libsecret-tools` on Debian/Ubuntu, `libsecret` on Arch) + unlocked Secret Service keyring (e.g. GNOME Keyring). No plaintext fallback.
- Linux audio needs **`pactl` and `parec`** (`pulseaudio-utils` on Debian/Ubuntu, `libpulse` on Arch) + running PulseAudio server or PipeWire with **`pipewire-pulse`**. Use recent `pactl` with JSON output (PulseAudio 15+). No root needed for capture.
- Native macOS capture build needs **Xcode Command Line Tools** (`xcode-select --install`). No third-party audio lib. Run `.app` bundle (below) for privacy permission requests.

## Build, test and run

```bash
dotnet build
dotnet test
dotnet run --project src/Tropicast.Station.App
```

Optimized build: `-c Release`. Debug builds enable Avalonia Developer Tools (F12).

For encoding, build pinned native bundle first (needs C compiler, make, Perl, pkg-config, curl, tar, xz-capable tar):

```bash
bash scripts/build-ffmpeg.sh linux-x64  # or linux-arm64, osx-arm64, osx-x64
dotnet build -c Release
```

Windows: run `bash scripts/build-ffmpeg.sh win-x64` from MSYS2 **MINGW64** shell with MinGW GCC/pkgconf, make, Perl, curl, tar. Script builds only FFmpeg/LAME/Opus + Linux OpenSSL, not .NET app. Native Linux builds use build host's libc baseline; build release bundles on oldest supported distro. Linux arm64 = native build, not x64 cross-build. macOS builds target either CPU arch, need Xcode tools. Sources checksum-pinned; intermediate outputs under ignored `artifacts/`.

## Solution layout

| Project                                        | Responsibility                                                                     |
| ---------------------------------------------- | ---------------------------------------------------------------------------------- |
| `src/Tropicast.Station.App`                    | Avalonia UI (views, view models), composition root (`AppHost`)                     |
| `src/Tropicast.Station.Core`                   | Domain model + shared services; no UI/platform deps                                |
| `src/Tropicast.Station.Audio`                  | Audio capture abstractions + platform adapters                                     |
| `src/Tropicast.Station.Audio.Windows`          | WASAPI shared-mode input + render-endpoint loopback via NAudio                     |
| `src/Tropicast.Station.Audio.Linux`            | PulseAudio/pipewire-pulse sources + sink monitors via libpulse clients             |
| `src/Tropicast.Station.Audio.MacOS`            | Core Audio input + ScreenCaptureKit system audio via native Apple-framework bridge |
| `src/Tropicast.Station.Encoding`               | Bounded float32→MP3/Opus FFmpeg children + credential-safe Icecast publishers      |
| `src/Tropicast.Station.Infrastructure`         | JSON profiles, OS credential stores, Icecast connection testing                    |
| `tests/Tropicast.Station.Core.Tests`           | Unit tests for class libs                                                          |
| `tests/Tropicast.Station.App.Tests`            | Headless Avalonia UI tests                                                         |
| `tests/Tropicast.Station.Audio.Tests`          | PCM conversion, synthetic capture, hot-plug lifecycle tests                        |
| `tests/Tropicast.Station.Audio.Windows.Tests`  | Windows adapter queue/lifecycle tests + opt-in hardware qualification              |
| `tests/Tropicast.Station.Audio.Linux.Tests`    | Linux parser/session tests + opt-in synthetic native capture integration           |
| `tests/Tropicast.Station.Audio.MacOS.Tests`    | macOS adapter lifecycle/permission tests + native enumeration smoke test           |
| `tests/Tropicast.Station.Encoding.Tests`       | Encoder settings, real bundled codec/lifecycle tests, opt-in Icecast listener POC  |
| `tests/Tropicast.Station.Infrastructure.Tests` | Persistence, source handshake, native credential-store tests                       |

Deps point inward: `App` → `Audio` / `Encoding` / `Infrastructure` → `Core`.

## Conventions

- **MVVM** with [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/).
  Bindings compiled by default, so views declare `x:DataType`.
- **Dependency injection, configuration and logging** via `Microsoft.Extensions.Hosting`. Each lib exposes `AddStation<Layer>()` registration extension; app wires them in `AppHost`.
- **Central package management**: versions in `Directory.Packages.props`.
- **Strict builds**: nullable reference types, recommended .NET analyzers, code style enforced at build (`.editorconfig`), warnings as errors.

## Continuous integration

GitHub Actions (`.github/workflows/ci.yml`) restores, builds, tests solution on `windows-latest`, `ubuntu-latest` and `macos-latest` for every push to `main` + every PR. UI tests use Avalonia Headless, no display server needed.

CI also exercises real OS credential stores with unique test entries deleted after (isolated keyring/keychain on Linux/macOS). Locally, those integration tests opt-in:

```bash
TC_TEST_OS_SECRETS=1 dotnet test
```

For optional real Icecast handshake test, start local Icecast with source username `source` + password `tc-test-source`, set `TC_TEST_ICECAST_PORT` to its port when running `dotnet test`. Uses unique temp mount; tests accepted creds, wrong creds, occupied mount, release after disconnect.

Independent restart test launches own loopback-only Icecast, stops/starts that exact server, verifies auto recovery with decodable listener audio, checks wrong creds terminal. Doesn't restart shared `TC_TEST_ICECAST_PORT` instance. Ubuntu CI runs it with `TC_TEST_ICECAST_EXECUTABLE=/usr/bin/icecast2`. Locally, opt in with native executable or existing Docker image:

```bash
TC_TEST_ICECAST_DOCKER_IMAGE=tropicast-icecast dotnet test \
  tests/Tropicast.Station.Encoding.Tests -c Release \
  --filter FullyQualifiedName~ReconnectIntegrationTests
# Native Linux alternative:
TC_TEST_ICECAST_EXECUTABLE=/usr/bin/icecast2 dotnet test \
  tests/Tropicast.Station.Encoding.Tests -c Release \
  --filter FullyQualifiedName~ReconnectIntegrationTests
```

Test uses known test-only passwords, synthetic tones, unique private container/process, ephemeral host port; removes own resources after, never records mics or restarts production server.

Same opt-in server harness verifies stream metadata against `status-json.xsl`, then decodes listener MP3 to check saved sample rate + channel count. Use `--filter FullyQualifiedName~Real_Tropicast_status` with either server env var above to run that acceptance check alone.

### End-to-end suite (Tropicast container)

`tests/Tropicast.Station.E2E.Tests` proves whole pipeline against real Tropicast Icecast image (built from `tests/Tropicast.Station.E2E.Tests/docker`): synthetic tone capture, bundled FFmpeg encoder, source handshake, listener whose MP3 decoded + checked for non-silent audio. Each test starts own container on ephemeral loopback port with fresh random creds, removes it after. Scenarios: go-live + stop, MP3 and Opus from one capture (both listeners decoded), unattended recovery after server restart, wrong password, mount already in use (first source keeps playing), unreachable host.

```bash
bash scripts/build-ffmpeg.sh linux-x64   # once
bash scripts/e2e.sh                      # builds the image, then runs the suite
```

Needs Docker. Without `TC_E2E_IMAGE` suite skips, so plain `dotnet test` stays fast; with `TC_E2E_REQUIRED=1` (set by `scripts/e2e.sh` and CI) missing image or FFmpeg bundle fails run. CI runs it in separate Ubuntu "End-to-end" job; failures name expected condition + broadcast's last state and message.
