using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tropicast.Station.Core.Settings;

namespace Tropicast.Station.App.ViewModels;

/// <summary>General preferences: for now, whether the app keeps running in the tray.</summary>
public sealed partial class GeneralSettingsViewModel(IAppSettingsStore store, ITraySupport tray) : ViewModelBase
{
    private AppSettings _settings = AppSettings.Default;
    private bool _loaded;

    /// <summary>Hide to the tray on close and minimize. Off until loaded, so nothing hides before the choice is known.</summary>
    [ObservableProperty] public partial bool KeepInTray { get; set; }

    /// <summary>False on a Linux desktop without a StatusNotifier host (no tray icon would show).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TrayHint))] public partial bool TrayDetected { get; set; } = true;

    public string TrayHint => TrayDetected
        ? "Closing or minimizing the window keeps Tropicast Station running in the system tray. Quit from the tray menu."
        : "No system tray was found on this desktop: with this on, a closed window could only come back by starting the app again.";

    public bool TrayNoticeShown => _settings.TrayNoticeShown;

    [RelayCommand]
    private async Task LoadAsync()
    {
        _settings = await store.LoadAsync();
        TrayDetected = await tray.IsAvailableAsync();
        _loaded = false;
        KeepInTray = _settings.KeepInTray ?? TrayDetected;
        _loaded = true;
    }

    partial void OnKeepInTrayChanged(bool value)
    {
        if (_loaded)
        {
            _ = SaveAsync(_settings with { KeepInTray = value });
        }
    }

    public Task MarkTrayNoticeShownAsync() => SaveAsync(_settings with { TrayNoticeShown = true });

    private async Task SaveAsync(AppSettings settings)
    {
        _settings = settings;
        try
        {
            await store.SaveAsync(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preference that cannot be saved still applies until the app quits.
        }
    }
}
