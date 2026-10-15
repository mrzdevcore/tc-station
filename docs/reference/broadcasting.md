# Broadcasting

## Tropicast account

The default way to broadcast: **Sign in with Tropicast**, pick a station, Go Live. No host, mount or password is typed. Contract: [tc-dashboard docs/desktop-api.md](https://github.com/tropicast/tc-dashboard/blob/main/docs/desktop-api.md) (v1).

- **Sign-in** uses the device authorization flow. The app shows a code such as `BCDF-GHJK` and opens the browser. The user approves the code on the web while signed in there, and the app polls until approved, refused or expired (10 minutes). The app never sees the account password. The device is named after the computer (`Environment.MachineName`).
- **Terms**: after sign-in and at each start, the app reads the current terms version from `GET /api/v1/auth/me`. A version newer than the one accepted on this computer shows the first-start screen again ([details](../packaging.md#terms-privacy-and-licenses-at-first-start)); an account that has not accepted it on the web is told to do so there. Offline, or with a server without terms, the bundled version applies.
- **Stations**: every station of every tenant of the account. **Refresh stations** reloads them. The choice is remembered.
- **Broadcast target**: on the first Go Live for a station, the app asks the API for the ingest URLs, username, formats and plan bitrate, and a password for this device. MP3 is published at up to 128 kbps, plus Ogg Opus at up to 64 kbps from the same capture when the plan includes it. Both are capped by the plan's maximum. If Icecast refuses the stored password (revoked on the web, or replaced by another install), the app gets a new one once, then shows the error.
- **Storage**: the refresh token and station passwords live only in the OS credential store. The access token (15 minutes) lives only in memory and is refreshed before it expires. Stations and targets without passwords are in `account.json` next to `profiles.json`.
- **Sign out** revokes the device on the server and removes its tokens and station passwords. Signing the device out on the web (Devices), or a password change, ends it too: the next API call signs the app out.
- **API address**: `https://app.tropicastradio.com`. For a local tc-dashboard (`compose.yaml`), set `Tropicast__ApiBaseUrl=http://localhost:8080`. Plain HTTP is accepted for this computer only.

**Manual connection profiles** (below) remain as an advanced option for your own Icecast server.

Opt-in contract test against a running tc-dashboard (`compose.yaml`, plus HTTPS on the API for the browser cookies):

```sh
TC_DASHBOARD_URL=http://localhost:8080 TC_DASHBOARD_WEB_URL=https://localhost:8443 TC_DASHBOARD_MAIL_URL=http://localhost:8025 \
  dotnet test tests/Tropicast.Station.Infrastructure.Tests --filter FullyQualifiedName~Contract
```

## Go Live / Stop

Window = sidebar (search, pages) + settings cards: one row per setting, title and description left, control right. Below 760 px wide the sidebar collapses to icons; Ctrl+F focuses search. **Broadcast** page: pick **a station of the Tropicast account** (or, under _manual connection profile_, a saved profile), pick **one input or system-output source** (Audio source page), press **Go Live** in the broadcast bar, which stays at the bottom of every page with state, elapsed time, reconnect countdown and **Retry now**. Manual profiles need credentials saved on the Connection profiles page first. Encoding needs an **audio/mpeg** or **audio/ogg** profile and the bundled FFmpeg ([publishing](publishing.md)). Starting broadcast replaces any active local preview with capture in selected encoder format.
Broadcast page shows selected saved profile's bitrate, rate, channels, stream name. Unsaved editor changes and source picker's **preview** rate/channels don't override saved broadcast settings.

Status badge/text expose **Idle → Connecting → Live → Stopping → Idle**, plus **Error** on capture/connection/encoder failure. **Live** begins when encoder sends MP3 bytes, not just on auth success. Elapsed counter accumulates actual Live time (excl. reconnect downtime), retains last session duration after stop.
An idle system-output source may not emit audio yet (macOS system audio): the broadcast stays Connecting until it does.
Transient publisher failures enter **Reconnecting** automatically. No silent source/profile fallback.

While Connecting/Live/Reconnecting/Stopping: profile editing/testing, source selection, encoder-format controls, local preview commands locked. **Stop broadcast** asks confirmation when live or reconnecting; **Keep broadcasting** (or Escape) cancels dialog. Stop while Connecting cancels startup without live-stream warning.
Quitting (tray **Quit**, or closing the window with the tray setting off) also asks before ending live stream; confirming stops capture + flushes/reaps FFmpeg before closing.
SIGTERM/OS/process termination still does host cleanup where possible, but can't always show interactive confirmation.

Tray/menu-bar uses Tropicast logo, red live dot for Live + Reconnecting, state/elapsed/countdown tooltip. Right-click menu (click on macOS): **Open Tropicast Station** (restores last size/state), **Maximize window**, **Go Live**, **Stop broadcast**, **Quit Tropicast Station**; left click shows or hides the window. With **Settings › General › Keep running in the tray when the window is closed or minimized** (default on for Windows/macOS; on Linux only when a StatusNotifier tray host answers on D-Bus), close and minimize hide the window to the tray and a live broadcast goes on; a one-time notice says so. **Quit** (tray, Cmd+Q, OS shutdown) asks before ending a live stream. Setting off: minimize to taskbar, close = quit after confirmation. Preference saved in `settings.json` next to the profiles. Buttons/selectors have screen-reader names; normal Tab nav + Enter/Space activate controls. Prominent Go Live/Stop controls stay visible while source settings scroll.

Headless tests cover profile/source prerequisites, keyboard start/stop, state text, selector locks, tray status, denied Stop/close, accepted close, capture removal, encoder errors. Opt-in Icecast suite also runs whole controller with synthetic capture → converter → FFmpeg → real listener, verifies decodable MP3 + stop. Not physical mic or manual platform accessibility qualification.

### Automatic reconnect

Dropped Icecast connection, network timeout, HTTP 5xx or unexpected publisher process exit triggers fresh encoder/source session on **same saved target**. Initial unreachable connections also retry. Backoff starts 1 s, doubles to 30 s max, ±20% jitter (still capped 30 s). Successful return to Live resets backoff.

Capture + PCM meters continue during reconnect; audio produced while publisher absent **not buffered or replayed**. UI shows upcoming attempt number + countdown, then connecting/waiting-for-audio status.
**Retry now** skips wait, no parallel handshake; disabled while new publisher connecting or waiting for first audio.
**Stop broadcast** and confirmed Quit cancel waits + in-flight handshakes, stop capture, release encoder.

Auth failures, TLS cert/config failures, occupied mounts, other HTTP 4xx, invalid/missing encoder config, queue overruns, capture/device failures = **terminal**. Show actionable Error + stop capture, no endless retry. Fix profile/source/bundle, Go Live again. Error classification uses typed source/encoder failures, never arbitrary server message text. Credentials stay managed, never in native process args or diagnostic logs.

UI + controller snapshot expose count of **successful reconnects** and cumulative **downtime after first Live**, incl. reconnect handshakes + waiting for audio. Counters keep last session values after Stop, reset on next Go Live. Initial connection wait ≠ live downtime.

## Connection profiles

Profile = name, hostname/IP (no scheme/port), port, mount path (e.g. `/live.mp3`), source username (`source` default), password, TLS choice, content type. Each profile also saves **MP3 bitrate** (64/96/128/192/320 kbps), **sample rate** (44100/48000 Hz), **channels** (1 mono / 2 stereo). Defaults 128 kbps, 44100 Hz, stereo. Existing profiles lacking these fields load with defaults; explicitly invalid values reported as corrupt/invalid, not silently replaced.

Optional **stream name**, **description**, **genre**, **website URL** identify stream to listeners. Profile name labels local saved connection; distinct from public stream name. Values sent with authenticated source handshake as `Ice-Name`, `Ice-Description`, `Ice-Genre`, `Ice-URL`, `Ice-Bitrate` and `Ice-Audio-Info`. Metadata resent every reconnect. Icecast's `/status-json.xsl` exposes `server_name`, `server_description`, `genre`, `server_url` and `bitrate` while mount live. Blank optional metadata omitted → server defaults.

Names/genres ≤128 chars, descriptions/URLs ≤512. Control chars rejected; website URLs must be absolute HTTP/HTTPS without credentials. Metadata public: never put passwords/secrets there. Encoder publishes MP3 (`audio/mpeg`) or Ogg Opus (`audio/ogg`, 48/64/96 kbps, always 48 kHz); `audio/aac` profiles are for connection testing only.

**Also publish Ogg Opus** (MP3 profiles whose mount ends in `.mp3`) adds a second stream from the same capture on the matching `.opus` mount (`/stations/42/live.mp3` → `/stations/42/live.opus`) at the profile's **Opus bitrate** (default 64 kbps; 48 kbps on the free plan). Each stream has its own FFmpeg child, `PUT` with its own `Content-Type`, `Ice-Bitrate` and `Ice-Audio-Info`, and its own reconnect backoff: losing or being refused one stream (e.g. the station's plan allows only MP3, so Tropicast answers 401 on `.opus`) does not stop the other. The broadcast stays **Live** while any stream is live, the Broadcast page lists each stream's state, and the broadcast ends in **Error** only when every stream has failed permanently.

**Save profile** writes non-secret settings to user's app-data dir:

- Windows: `%APPDATA%\Tropicast\Station\profiles.json`
- Linux: `$XDG_CONFIG_HOME/Tropicast/Station/profiles.json` (normally `~/.config`)
- macOS: `~/Library/Application Support/Tropicast/Station/profiles.json`

Passwords stored separately in Windows Credential Manager, macOS Keychain, or Linux Secret Service (libsecret). Saved passwords never loaded into editor; leave password blank to keep existing. Delete removes selected profile + its credential. Failed settings write tries to restore previous credential.

Use TLS for non-local connections: without TLS, HTTP Basic source credentials + audio travel unencrypted. TLS certs must be trusted + match host; app doesn't bypass cert checks.

**Test connection** uses current editor values (unsaved). Does authenticated Icecast `PUT` with `Expect: 100-continue`, sends no audio, closes immediately. Briefly reserves free mount; success = point-in-time check, not reservation for later broadcast. Auth failure (401), mount in use (409), publishing denied (403), network failure, TLS errors reported separately. Don't use Icecast admin or shared production source password.

Broadcasting resolves saved profile via `IBroadcastTargetProvider`. `ManualBroadcastTargetProvider` rejects invalid profiles + missing credentials before returning ephemeral target; future API-backed provider can replace it without changing capture pipeline.
