using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tropicast.Station.Core.Settings;

namespace Tropicast.Station.App.ViewModels;

/// <summary>
/// The first-run screen (#68): the privacy summary, the Terms of Service and Privacy Policy, and the third-party
/// licenses. Until it is accepted nothing else starts (account, profiles, audio devices, tray icon); it shows again
/// when a newer terms version ships with the app or comes from the account.
/// </summary>
public sealed partial class ConsentViewModel(GeneralSettingsViewModel settings, IExternalBrowser browser, LegalLinks links,
    TimeProvider time) : ViewModelBase
{
    private readonly TaskCompletionSource _accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _licenses;

    /// <summary>The version the user is asked to accept: the bundled one, or a newer one the account reported.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(VersionText))] public partial string Version { get; private set; } = LegalTerms.Version;

    /// <summary>The screen covers the window.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBlocking))] public partial bool IsShown { get; private set; }

    /// <summary>Opened again from About: it closes instead of quitting.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBlocking), nameof(DismissLabel), nameof(Heading))]
    public partial bool IsReview { get; private set; }

    /// <summary>The changed terms of an earlier acceptance, not a first run.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Heading))] public partial bool IsUpdate { get; private set; }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ContinueCommand))] public partial bool IsAccepted { get; set; }

    /// <summary>The third-party licenses replace the summary on the screen.</summary>
    [ObservableProperty] public partial bool ShowsLicenses { get; set; }

    [ObservableProperty] public partial string Status { get; private set; } = "";

    /// <summary>Completes once current terms are accepted (at once when they already were).</summary>
    public Task Accepted => _accepted.Task;

    /// <summary>The screen must be answered before anything else (not a review).</summary>
    public bool IsBlocking => IsShown && !IsReview;

    public string Heading => IsReview ? "Terms, privacy and licenses"
        : IsUpdate ? "The Terms of Service have changed" : "Welcome to Tropicast Station";

    public string DismissLabel => IsReview ? "Close" : "Quit";

    public string VersionText => $"Version {Version}";

    /// <summary>For Settings › About.</summary>
    public string AcceptedSummary => settings.Consent is { } consent
        ? $"Version {consent.Version}, accepted on {consent.AcceptedAt.ToLocalTime():d MMMM yyyy}."
        : "Not accepted yet.";

    public Uri TermsOfService => links.TermsOfService;
    public Uri PrivacyPolicy => links.PrivacyPolicy;

    /// <summary>The bundled THIRD_PARTY_NOTICES (FFmpeg, LAME, Opus, OpenSSL).</summary>
    public string Licenses => _licenses ??= ReadLicenses();

    /// <summary>Quit chosen on the first-run screen: the app exits with nothing stored.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Once the saved settings are read: shows the screen unless the current terms were accepted.</summary>
    public void Check()
    {
        OnPropertyChanged(nameof(AcceptedSummary));
        if (LegalTerms.IsNewer(Version, settings.Consent?.Version))
        {
            Show(review: false);
        }
        else
        {
            _accepted.TrySetResult();
        }
    }

    /// <summary>The account reported a terms version: a newer one than the accepted one asks again.</summary>
    public void ApplyAccountVersion(string version)
    {
        if (!LegalTerms.IsValid(version) || !LegalTerms.IsNewer(version, Version))
        {
            return;
        }
        Version = version;
        if (settings.Consent is { } consent && LegalTerms.IsNewer(version, consent.Version) && !IsBlocking)
        {
            Show(review: false);
        }
    }

    private void Show(bool review)
    {
        IsUpdate = settings.Consent is not null;
        IsReview = review;
        IsAccepted = review && !LegalTerms.IsNewer(Version, settings.Consent?.Version);
        ShowsLicenses = false;
        Status = "";
        IsShown = true;
    }

    [RelayCommand(CanExecute = nameof(IsAccepted))]
    private async Task ContinueAsync()
    {
        if (!IsReview || LegalTerms.IsNewer(Version, settings.Consent?.Version))
        {
            await settings.SaveConsentAsync(new ConsentRecord(Version, time.GetUtcNow()));
            OnPropertyChanged(nameof(AcceptedSummary));
        }
        IsShown = false;
        IsReview = false;
        _accepted.TrySetResult();
    }

    /// <summary>Quit on a first run or changed terms; Close on a review.</summary>
    [RelayCommand]
    private void Dismiss()
    {
        if (IsReview)
        {
            IsShown = false;
            IsReview = false;
        }
        else
        {
            QuitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Settings › About: the screen again, to read the texts.</summary>
    [RelayCommand]
    private void Review()
    {
        if (!IsShown)
        {
            Show(review: true);
        }
    }

    /// <summary>Settings › About: straight to the third-party licenses.</summary>
    [RelayCommand]
    private void ReviewLicenses()
    {
        Review();
        ShowsLicenses = true;
    }

    [RelayCommand]
    private void ToggleLicenses() => ShowsLicenses = !ShowsLicenses;

    [RelayCommand]
    private void OpenTerms() => Open(links.TermsOfService);

    [RelayCommand]
    private void OpenPrivacy() => Open(links.PrivacyPolicy);

    private void Open(Uri page)
    {
        try
        {
            browser.Open(page);
            Status = "";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            Status = $"Open {page} in your browser.";
        }
    }

    private static string ReadLicenses()
    {
        using var stream = typeof(ConsentViewModel).Assembly.GetManifestResourceStream("THIRD_PARTY_NOTICES");
        if (stream is null)
        {
            return "The third-party notices are in the THIRD_PARTY_NOTICES file next to the app.";
        }
        using var reader = new StreamReader(stream);
        return "Tropicast Station: Copyright 2026 Tropicast. Apache License 2.0.\n\n" + reader.ReadToEnd();
    }
}
