namespace Tropicast.Station.Core.Settings;

/// <summary>App preferences that are neither profiles, secrets nor account data.</summary>
/// <param name="KeepInTray">Keep running in the system tray when the window is closed or minimized; null until the user chooses (a per-platform default applies).</param>
/// <param name="TrayNoticeShown">Whether the "still running in the tray" notice was shown once.</param>
public sealed record AppSettings(bool? KeepInTray = null, bool TrayNoticeShown = false)
{
    public static AppSettings Default { get; } = new();
}

public interface IAppSettingsStore
{
    /// <summary>The saved settings, or <see cref="AppSettings.Default"/> when there are none or they cannot be read.</summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
