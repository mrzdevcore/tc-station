# Audio capture and levels

## Audio sources and preview

**Audio source** page groups devices into **Microphones / inputs** and **Application / system output**. Pick one device, encoder sample rate (44.1 or 48 kHz), mono/stereo, then **Start preview**. Consumes captured PCM through shared conversion pipeline; no playback, encoding, or Icecast publish. Levels: see [Audio levels and warnings](#audio-levels-and-warnings); publishing: **Go Live** ([broadcasting](broadcasting.md)).

Windows: lists active WASAPI inputs + playback devices (loopback sources). Linux: PulseAudio/PipeWire inputs + sink monitor sources. macOS: Core Audio inputs (incl. virtual) + ScreenCaptureKit **System audio** source.
Run synthetic adapter on any OS for demos:

```bash
dotnet run --project src/Tropicast.Station.App -- --demo-audio
```

Demo mode supplies 440 Hz, 44.1 kHz mono signed-16 input + 660 Hz, 48 kHz stereo float32 loopback source, both labelled **Demo**. Generated tones, not mic/app recordings.

### Adapter contract and pipeline

`IAudioCaptureProvider` exposes enumeration, `DevicesChanged` notifications, `StartAsync`. `AudioDevice` carries stable ID, display name, kind, default flag, native `AudioFormat`. `IAudioCaptureSession` delivers interleaved little-endian signed16 or float32 PCM via single-consumer async stream. Each `PcmFrame` owns its buffer; adapters must not reuse memory. Stop, dispose, reader cancel must unblock capture, safe concurrently/repeatedly. Bounded native queues must report overruns, not silently drop audio. Device loss throws `IOException`.

`AudioCaptureService` serializes lifecycle/device refreshes, cancels + disposes capture on removal or native-format change, emits visible error without switching device. Idle hot-plug/default-device changes auto-refresh picker. Selected device preserved by ID on rename. Windows WASAPI + Linux PulseAudio adapters registered before shared audio services; macOS Core Audio adapter same pattern. Use `TryAdd` registration so explicitly registered adapter not overwritten.

`PcmConverter` produces float32 mono/stereo PCM for future encoder via streaming windowed-sinc low-pass resampler, 32 input-frame lookahead (~0.73 ms at 44.1 kHz). Mono duplicated to stereo; stereo averaged to mono. More channels: mono averages all; stereo averages alternating channel indices. Deliberately **not** speaker-layout-aware surround downmix. No gain/limiting. Conversion state carries across chunks; `Flush()` drains finite source, device-loss/stop discards tail. Invalid PCM alignment, non-finite float samples, midstream format changes → explicit errors.

`AudioCaptureService.FrameAvailable` = future encoder integration point: frames already in requested format. Subscribers run on capture thread, must not block or throw. Marshal UI work to UI dispatcher; keep encoder handoff bounded, report overloads. `ToneAudioCaptureProvider` also exposes `SetDevices` for deterministic hot-plug tests/demos.

### Windows WASAPI capture

App auto-selects `WindowsAudioCaptureProvider` on Windows unless `--demo-audio` passed. Active capture endpoints (incl. USB mixer/interface inputs) and render endpoints listed separately; Windows **Multimedia** default labelled. Capture targets explicit endpoint ID, never silently follows different default. Add/remove/state/default/property notifications (NAudio `MMDeviceNotificationClient`) trigger refresh. Removing active endpoint stops capture + shows guidance; replug refreshes picker.

Render-endpoint loopback captures **all audio played through that endpoint**, not single app/process. Pick endpoint your player uses. WASAPI delivers no loopback packets while the render endpoint is idle, so the adapter plays an inaudible silent stream on that endpoint for the capture's lifetime: loopback then reports silent packets (encoded as zeros) and the broadcast keeps flowing. The mix is unchanged.

Capture uses WASAPI **shared mode**, 100 ms native buffers, worker-thread init (no captured UI sync context). PCM16 + float32 mix formats preserved. PCM24/PCM32 mix formats request float32 via WASAPI shared-mode conversion, keeping endpoint rate/channel count. Picker format therefore = PCM supplied by adapter, not necessarily physical bit depth. Common `PcmConverter` converts to chosen encoder format. Non-PCM formats + rates/channel counts outside shared contract rejected explicitly.

Captured through NAudio 3 `WasapiRecorder` (shared mode, polling, 100 ms buffers); each callback packet copied before the callback returns, and packets WASAPI flags silent become zeros. Bounded queue (256 packets, max 2 s PCM by byte budget) fails explicitly on overrun, no silent drop. Capture errors + device invalidation close stream; cancel/stop unblocks readers; dispose joins native capture thread on worker before releasing endpoint.

If Windows denies mic access, app shows settings path: **Settings → Privacy & security → Microphone** (Windows 10: **Settings → Privacy → Microphone**). Enable **Microphone access** and **Let desktop apps access your microphone**. Adapter checks explicit Windows mic-consent denials as preflight hint, then treats WASAPI access-denied as authoritative. Loopback not blocked by mic-only denial. Exclusive-device use, stopped Windows Audio service, unsupported format, invalidation have separate guidance.

### Windows hardware qualification (not exercised by hosted CI)

Hosted CI runs adapter with injected fake native backend: can't verify USB interfaces or audible glitches. On Windows station, run app without `--demo-audio` to check real mic/interface + playback endpoint. For sustained qualification, feed continuous audio into input and play continuous audio through loopback endpoint for whole test.

Set endpoint IDs (shown in test output; also exposed by `GetDevicesAsync`), run opt-in test in PowerShell:

```powershell
$env:TC_TEST_WASAPI = "1"
$env:TC_WASAPI_INPUT_ID = "<USB interface capture endpoint ID>"
$env:TC_WASAPI_LOOPBACK_ID = "<player render endpoint ID>"
$env:TC_WASAPI_DURATION_SECONDS = "1800"
dotnet test tests/Tropicast.Station.Audio.Windows.Tests -c Release `
  --filter FullyQualifiedName~WindowsHardwareTests --logger "console;verbosity=detailed"
```

Test captures each endpoint 30 min (1 h total), requires ≥98% expected PCM frame count, fails on malformed packets, capture errors, queue overruns. Continuity check, **not proof that the result is glitch-free**: listen to/analyze an encoded recording (Go Live to a test station) during station qualification.

NAudio.Wasapi/NAudio.Core 3.1.0 MIT-licensed; package license metadata + upstream attribution at <https://github.com/naudio/NAudio>.

### Linux PulseAudio / PipeWire capture

App auto-selects `LinuxAudioCaptureProvider` on Linux unless `--demo-audio` passed. Supervises `pactl` (JSON enumeration + topology subscription) and `parec` (raw PCM capture), both libpulse clients. Same PulseAudio protocol on PulseAudio and PipeWire's `pipewire-pulse` compat server; no shell commands or device-name interpolation.

Mics + mixer/interface sources under inputs. Sink **monitor** sources under system output: pick monitor of output your player uses. Monitor captures whole sink, not one app. Route player to dedicated sink for app-only capture. Suspended/idle sources stay selectable; recording starts only on **Start preview**.

Default source/sink changes auto-refresh default labels. Selection pinned to explicit source name, not silently following new default. `stream.dont-move=true` prevents auto fallback when selected endpoint vanishes. Device removal or native format change stops preview with error; replug updates picker. Audio-server/subscription failures visible; notifications reconnect every 2 s. Missing packages, server access failures, capture failures report guidance, not empty success.

`parec` supplies float32 LE PCM at source's advertised rate + channel count (libpulse converts original encoding). Shared converter handles encoder target format. Capture requests 40 ms server latency + 20 ms processing; actual latency server-dependent. Owned 20 ms packets enter bounded 100-packet queue (~2 s); overload fails explicitly. Stop/cancel terminates owned child, dispose reaps it; shutdown also terminates topology subscription.

Run synthetic native integration test on running audio server:

```bash
TC_TEST_PULSE=1 dotnet test tests/Tropicast.Station.Audio.Linux.Tests -c Release
```

Test also needs `pacat` (same utils/libpulse package). Creates uniquely named null sink + remapped input, plays only generated 600 Hz tone into sink, checks input/monitor signal + stop/restart, then removes sources and checks auto capture shutdown. Doesn't record personal mics or change desktop defaults; removes its modules/child processes after. CI runs against isolated PulseAudio server; `TC_TEST_PULSE_DEFAULTS=1` also checks default changes — enable only on isolated server. PipeWire capture also exercised locally. Real USB/mixer hardware + sustained audible-glitch qualification remain station-side; synthetic tests don't prove those.

Audio preview stays local-only. Use **Go Live** in the broadcast bar to publish selected source via encoding backend.

### macOS Core Audio / system audio capture

Build app bundle **on a Mac**, launch it instead of `dotnet run` for native capture. `osx-arm64` on Apple Silicon, `osx-x64` on Intel:

```bash
bash scripts/build-macos.sh osx-arm64
open "artifacts/osx-arm64/Tropicast Station.app"
```

Script publishes self-contained .NET app + ad-hoc signs bundle. Native Objective-C bridge universal (arm64/x86_64); CI compiles it + builds both app archs. Bundle includes mic + screen/system-audio usage descriptions. Unbundled execution refuses permission requests with guidance rather than letting macOS kill process missing privacy declarations. Ad-hoc builds = dev, not distribution; stable dev signing/notarization part of packaging (#14); rebuild may need reauthorizing privacy permissions.

Core Audio enumerates live input devices by persistent device UID, native rate, input channel count. Includes built-in mics, USB interfaces, virtual inputs. Audio Queue records interleaved float32 at that rate/channel count; Core Audio converts hardware encoding. Device/default/format changes polled every 2 s. Default changes update labels, never change selected source. Device removal or format change stops capture with error instead of switching mic.

**Start preview** requests mic access only for input capture. If denied, enable **Tropicast Station** under **System Settings → Privacy & Security → Microphone**, restart app. No audio recorded during enumeration. OS permission dialog must be answered before pending start completes.

**System audio (ScreenCaptureKit)** source captures whole-system playback at 48 kHz stereo on macOS 13+, excluding this app's own playback. Needs Screen Recording permission on macOS 13; newer macOS labels it **Screen & System Audio Recording**. macOS may prompt; if capture fails, enable Tropicast Station there + restart. Display must be available. No screen output callback registered; no screen images retained, displayed, encoded or broadcast. Whole-system capture, not per-app selection or separate loopback per output. Idle output may give no samples.

For headless use, unavailable/denied system capture, or app-only routing, use virtual input like [BlackHole](https://github.com/ExistentialAudio/BlackHole). After installing driver, route player output to BlackHole, select **BlackHole input** in Tropicast. To also hear locally, create Multi-Output Device in Audio MIDI Setup combining BlackHole + speakers; match sample rates, configure drift correction there. Tropicast doesn't install drivers, alter output routing, or auto-mix mic with system audio. Virtual inputs still need mic permission.

Native callbacks copied into owned PCM packets. Bounded queue (256 packets, max 2 s by byte budget) reports overload instead of dropping samples. System audio's planar float buffers interleaved before shared converter. Stop/cancel closes reader immediately, releases native capture on worker; dispose drains callbacks before managed delegates released. Failed native start + shutdown errors surfaced explicitly.

CI exercises managed lifecycle, permission/failure mapping, hot-plug/default refresh, native permission-free enumeration; can't grant interactive privacy permissions or verify physical mics/interfaces. **Real capture and hardware qualification are pending on a Mac.** Check signed dev bundle with built-in mic + external interface, permission grant/denial, system playback, USB unplug/replug, default changes during capture, stop/restart, sustained preview on both hardware archs. PCM preview not broadcasting; use separate **Go Live** control to publish.

## Audio levels and warnings

**Levels & silence** page shows per-channel **peak** (amber) + **RMS** (green) meters, clipping and silence warning settings; the broadcast bar at the bottom of every page keeps compact RMS meters. **Preview** checks levels without transmitting; **Go Live** monitors exact normalized float32 PCM feeding encoder (after channel mapping + resampling, before MP3 compression). Levels refresh 25 Hz, in dBFS; zero audio shown at -90 dBFS meter floor. Numeric readings can exceed 0 dBFS though bars stop at 0.

**CLIPPING** shows per channel for samples ≥ -0.1 dBFS, 2 s hold refreshed by further clipping. **SILENCE** shows when every channel stays below configured RMS threshold for configured duration. Defaults -50 dBFS, 5 s; expand **Silence warning settings** to pick -90 to -10 dBFS and 1–60 s. Changes restart silence countdown. Silent loopback endpoint delivering no packets also detected. Signal recovery clears silence warning; stop, device loss, or new capture clears readings + clipping hold.

Optional **in-app notification** off by default, fires once per silence episode, not every meter tick. Warnings work in preview + live, never stop stream. Session-only meter prefs reset on app restart; independent of saved profile encoder settings.
