# Logging and diagnostics

**Diagnostics** page shows user log dir; **Export diagnostics** opens ZIP save dialog. Nothing uploaded automatically. Bundle has `diagnostics.json` (app/OS/runtime versions, arch, anonymous device kinds/default flags/native formats, broadcast state, reconnect/downtime counters) and `logs/recent.jsonl` (up to 5 recent log files).

Logs use .NET `ILogger` structured events + small rolling JSON-lines provider; no new logging framework. Files roll at **1 MiB**, **10 files retained**, in:

- Windows: `%LOCALAPPDATA%\Tropicast\Station\logs`
- Linux: `$XDG_STATE_HOME/Tropicast/Station/logs`, normally `~/.local/state/Tropicast/Station/logs`
- macOS: `~/Library/Logs/Tropicast/Station`

Files created owner read/write; log dir owner-only on Unix. Each process gets unique filenames. Recent records flushed synchronously; writes/rotation serialized. Logging = Information+; broadcast state changes recorded, not every meter tick. Disk/permission failures set explicit failure flag + emit fixed credential-safe stderr warning; export reports logging failure.

**Every configured sink**, incl. stderr, uses same fail-closed projection. Keeps only timestamp, severity, approved category, numeric event ID, approved error type, approved numeric counters. Raw formatted messages, templates, scopes, arbitrary structured fields, exception messages, stack traces, native stderr, URLs, FFmpeg command lines omitted—not regex-redacted after logging. Also protects credentials not yet loaded into app, encoded credentials, malicious diagnostic text. Avalonia's unfiltered trace sink disabled. App-owned source classes + runtime messages use **Tropicast** naming; external Icecast commands/schema/headers + technical refs keep actual names.

Events `1301` (bundle assembled), `1302` (broadcast state/counters), `1303` (unhandled error), `1304` (export failure) support diagnosis. Broadcast state numbers: Idle=0, Connecting=1, Live=2, Reconnecting=3, Stopping=4, Error=5. Existing capture/profile/encoder warnings keep category + approved error type.

Export excludes profiles/passwords, endpoints, stream metadata, device names/stable IDs, usernames, machine names, user paths, raw errors, audio. Recent logs parsed + re-projected before export, so manually added free text/fields can't leak. Corrupt/oversized logs → visible export failure, not incomplete success. Device identities intentionally anonymous; users can describe hardware separately.

Unhandled dispatcher errors + unobserved task failures stop capture/publishing where possible, show generic restart-required dialog, quit. Handlers detached on exit. Fatal CLR/process errors can't reliably show/await dialog: handler synchronously records safe error type, emits fixed restart message, exits code 1 before runtime can print raw managed exception details. Native fatal errors outside this managed handler's control. Startup errors also return exit code 1 with safe logs/stderr; no secret-bearing exception text shown. Handlers don't claim to recover corrupted state.

Automated tests inject credential-bearing messages, scopes, command lines, URLs, exceptions, edited log records; verify file/console sinks, rolling bounds/private permissions, ZIP contents, real FFmpeg live-session password absence, actual dispatcher error dialog/cleanup.
