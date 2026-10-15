using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tropicast.Station.Core.Settings;

namespace Tropicast.Station.App.ViewModels;

/// <summary>General preferences: whether the app keeps running in the tray, and the accepted terms (<see cref="ConsentViewModel"/>).</summary>
public sealed partial class GeneralSettingsViewModel(IAppSettingsStore store, ITraySupport tray) : ViewModelBase
{
    private AppSettings _settings = AppSettings.Default;
    private bool _loaded;
    private bool _applying;
    private bool? _choiceBeforeLoad;
    private Task _saving = Task.CompletedTask;
    private readonly TaskCompletionSource _stored = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Hide to the tray on close and minimize. Off until loaded, so nothing hides before the choice is known.</summary>
    [ObservableProperty] public partial bool KeepInTray { get; set; }

    /// <summary>False on a Linux desktop without a StatusNotifier host (no tray icon would show).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TrayHint))] public partial bool TrayDetected { get; set; } = true;

    public string TrayHint => TrayDetected
        ? "Closing or minimizing the window keeps Tropicast Station running in the system tray. Quit from the tray menu."
        : "No system tray was found on this desktop: with this on, a closed window could only come back by starting the app again.";

    public bool TrayNoticeShown => _settings.TrayNoticeShown;

    /// <summary>The accepted terms; read it once <see cref="Stored"/> has completed.</summary>
    public ConsentRecord? Consent => _settings.Consent;

    /// <summary>Completes once the saved settings are read (before the tray check).</summary>
    public Task Stored => _stored.Task;

    /// <summary>Applies the saved choice at once; the tray check (up to 2 s on Linux) only decides an unsaved default and the hint.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        _settings = await store.LoadAsync();
        _stored.TrySetResult();
        if (_settings.KeepInTray is { } saved && _choiceBeforeLoad is null)
        {
            Apply(saved);
        }
        TrayDetected = await tray.IsAvailableAsync();
        if (_choiceBeforeLoad is { } choice)
        {
            // Toggled while loading: the user's choice wins and is saved.
            _loaded = true;
            await Save(_settings with { KeepInTray = choice });
            return;
        }
        Apply(_settings.KeepInTray ?? TrayDetected);
    }

    private void Apply(bool value)
    {
        _applying = true;
        KeepInTray = value;
        _applying = false;
        _loaded = true;
    }

    partial void OnKeepInTrayChanged(bool value)
    {
        if (_applying)
        {
            return;
        }
        if (_loaded)
        {
            _ = Save(_settings with { KeepInTray = value });
        }
        else
        {
            _choiceBeforeLoad = value;
        }
    }

    public Task MarkTrayNoticeShownAsync() => Save(_settings with { TrayNoticeShown = true });

    /// <summary>Records the accepted terms, after the saved settings are read so no other preference is lost.</summary>
    public async Task SaveConsentAsync(ConsentRecord consent)
    {
        await Stored;
        await Save(_settings with { Consent = consent });
        OnPropertyChanged(nameof(Consent));
    }

    /// <summary>Saves one at a time, each write taking the latest settings, so an older value never lands last.</summary>
    private Task Save(AppSettings settings)
    {
        _settings = settings;
        _saving = WriteAfterAsync(_saving);
        return _saving;
    }

    private async Task WriteAfterAsync(Task previous)
    {
        await previous;
        try
        {
            await store.SaveAsync(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preference that cannot be saved still applies until the app quits.
        }
    }
}
