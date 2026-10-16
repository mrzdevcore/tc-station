# Install Tropicast Station

Download the file for your computer, check it, install it, then sign in.

| Computer | File |
|---|---|
| Windows 10 (1809+) or 11, x64 | `Tropicast-Station-<version>-win-x64.msix` (plus `tropicast.cer` and `install-windows-msix.ps1` if Windows refuses it) |
| Mac with Apple silicon (M1 or later) | `Tropicast-Station-<version>-osx-arm64.dmg` |
| Mac with an Intel processor | `Tropicast-Station-<version>-osx-x64.dmg` |
| Linux x86-64 | `Tropicast-Station-<version>-x86_64.AppImage` |

## 1. Check the download

Compare the file's SHA-256 with its line in `SHA256SUMS.txt`. They must be identical; if not, download again.

```powershell
Get-FileHash .\Tropicast-Station-<version>-win-x64.msix   # Windows (PowerShell)
```

```sh
shasum -a 256 Tropicast-Station-<version>-osx-arm64.dmg   # macOS
sha256sum Tropicast-Station-<version>-x86_64.AppImage     # Linux
```

## 2. Install

**Windows.** Double-click the `.msix` and choose **Install**. If Windows says the publisher or its
certificate is not trusted (error `0x800B0109` or `0x800B010A`), put the `.msix`, `tropicast.cer`
and `install-windows-msix.ps1` in one folder, open PowerShell **as administrator** in that folder and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-windows-msix.ps1 -Package .\Tropicast-Station-<version>-win-x64.msix -Certificate .\tropicast.cer
```

The script trusts the certificate on this computer (`LocalMachine\TrustedPeople`) and installs the
app. `-ExecutionPolicy Bypass` applies to this run only.

**macOS.** Open the `.dmg` and drag **Tropicast Station** to **Applications**. The first time, if
macOS says the app cannot be opened or its developer cannot be verified: right-click the app in
Applications, choose **Open**, then **Open** again. On macOS 15 or later, open
**System Settings › Privacy & Security** and click **Open Anyway** under the message about the app.

**Linux.** Make the file executable and run it:

```sh
chmod +x Tropicast-Station-<version>-x86_64.AppImage
./Tropicast-Station-<version>-x86_64.AppImage
```

It needs `pactl` and `parec` (package `pulseaudio-utils`, or `libpulse` on Arch) with PulseAudio or
PipeWire, and `secret-tool` (`libsecret-tools`) with an unlocked keyring.

## 3. Sign in

At the first start, accept the Terms of Service and the Privacy Policy. Then click **Sign in with
Tropicast**: the app shows a code and opens your browser. Sign in to `app.tropicastradio.com` there
and approve the code.
Allow the microphone (and, on macOS, screen and system audio recording) when asked.

## Troubleshooting

| Problem | What to do |
|---|---|
| Windows: `0x800B0109`, `0x800B010A`, "untrusted" or "publisher" error | Run `install-windows-msix.ps1` as above, from PowerShell opened **as administrator**. |
| Windows: "running scripts is disabled on this system" | Start the script with `powershell -ExecutionPolicy Bypass -File ...` as above. |
| Windows: "Run this script from an elevated PowerShell" | Close PowerShell, right-click it, choose **Run as administrator**, try again. |
| Windows: remove the app and the certificate | `.\install-windows-msix.ps1 -Package .\Tropicast-Station-<version>-win-x64.msix -Remove` (as administrator). |
| macOS: "damaged" or "cannot be opened" | Right-click › **Open**, or **Open Anyway** in Privacy & Security. Check you took the right `.dmg` for your Mac (Apple menu › About This Mac › Chip). |
| macOS: no sound captured | **System Settings › Privacy & Security**: allow Tropicast Station under **Microphone** and **Screen & System Audio Recording**, then restart the app. |
| Linux: "dlopen(): error loading libfuse.so.2" | Install `libfuse2` (Debian/Ubuntu: `sudo apt install libfuse2`), or run the file with `--appimage-extract-and-run`. |
| Linux: sign-in is not remembered | Install `libsecret-tools` and unlock your keyring (GNOME Keyring or KWallet). |
| No audio level, or no devices listed | Check the device in your system's sound settings first, then pick it again in the app. |
| The code is no longer accepted | Click **Sign in with Tropicast** again for a new code; each code lasts a few minutes. |

**Still stuck?** In the app, open **Diagnostics › Export diagnostics** and save the file (it contains
no password or token). Send it with the app version (**About**) and your system to support, or open
an issue at <https://github.com/tropicast/tc-station/issues>.
