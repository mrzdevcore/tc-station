using System.Diagnostics;

namespace Tropicast.Station.App;

/// <summary>Whether this desktop shows tray icons, which decides the default of "keep running in the tray".</summary>
public interface ITraySupport
{
    Task<bool> IsAvailableAsync();
}

/// <summary>
/// Windows and macOS always have a notification area or menu bar. On Linux a tray icon needs a StatusNotifier host
/// (KDE, Xfce, Cinnamon, GNOME with an AppIndicator extension…); without one, hiding the window would leave no way back.
/// </summary>
internal sealed class TraySupport : ITraySupport
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public async Task<bool> IsAvailableAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            return true;
        }
        try
        {
            using var process = Process.Start(new ProcessStartInfo("dbus-send",
            [
                "--session", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus",
                "org.freedesktop.DBus.NameHasOwner", "string:org.kde.StatusNotifierWatcher",
            ])
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            });
            if (process is null)
            {
                return false;
            }
            using var timeout = new CancellationTokenSource(Timeout);
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0 && output.Contains("boolean true", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            // No dbus-send, no session bus, or no answer: assume no tray, so closing keeps quitting as before.
            return false;
        }
    }
}
