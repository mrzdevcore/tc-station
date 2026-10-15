namespace Tropicast.Station.Core.Settings;

/// <summary>App preferences that are neither profiles, secrets nor account data.</summary>
/// <param name="KeepInTray">Keep running in the system tray when the window is closed or minimized; null until the user chooses (a per-platform default applies).</param>
/// <param name="TrayNoticeShown">Whether the "still running in the tray" notice was shown once.</param>
/// <param name="Consent">The Terms of Service and Privacy Policy accepted on this computer; null until the first-run screen is accepted.</param>
public sealed record AppSettings(bool? KeepInTray = null, bool TrayNoticeShown = false, ConsentRecord? Consent = null)
{
    public static AppSettings Default { get; } = new();
}

/// <summary>The version of the Terms of Service and Privacy Policy the user accepted, and when.</summary>
public sealed record ConsentRecord(string Version, DateTimeOffset AcceptedAt);

/// <summary>The Tropicast Terms of Service and Privacy Policy version this build ships with (#68).</summary>
public static class LegalTerms
{
    /// <summary>tc-dashboard's <c>Legal.TermsVersion</c> when this build was made: a date, <c>yyyy-MM-dd</c>.</summary>
    public const string Version = "2026-10-14";

    /// <summary>Whether <paramref name="version"/> has the date form versions use (anything else from a server is ignored).</summary>
    public static bool IsValid(string? version)
        => version is { Length: 10 } && DateOnly.TryParseExact(version, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _);

    /// <summary>Whether <paramref name="version"/> is newer than the <paramref name="accepted"/> one (or nothing was accepted).</summary>
    public static bool IsNewer(string version, string? accepted)
        => accepted is null || string.CompareOrdinal(version, accepted) > 0;
}

public interface IAppSettingsStore
{
    /// <summary>The saved settings, or <see cref="AppSettings.Default"/> when there are none or they cannot be read.</summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
