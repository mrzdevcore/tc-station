using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tropicast.Station.App.ViewModels;
using Tropicast.Station.App.Views;
using Tropicast.Station.Audio;
using Tropicast.Station.Core.Account;
using Tropicast.Station.Core.Profiles;
using Tropicast.Station.Core.Settings;
using Tropicast.Station.Tests;

namespace Tropicast.Station.App.Tests;

public sealed class ConsentTests
{
    [AvaloniaFact]
    public async Task First_run_shows_the_screen_before_anything_else_and_continue_records_the_version()
    {
        var (host, window, settings, profiles, _) = Open(AppSettings.Default);
        using (host)
        {
            var model = host.Services.GetRequiredService<MainViewModel>();
            await UntilAsync(() => model.Consent.IsShown);

            Assert.True(window.Find<ConsentView>("ConsentOverlay")!.IsEffectivelyVisible);
            Assert.False(window.Find<SplitView>("Shell")!.IsEffectivelyEnabled);
            Assert.Equal("Welcome to Tropicast Station", model.Consent.Heading);
            Assert.Equal("Quit", model.Consent.DismissLabel);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            // Nothing loaded, captured or stored before Continue.
            Assert.Equal(0, profiles.Loads);
            Assert.Empty(model.Audio.Inputs);
            Assert.Null(settings.Saved);
            Assert.False(model.Consent.ContinueCommand.CanExecute(null));

            model.Consent.IsAccepted = true;
            await model.Consent.ContinueCommand.ExecuteAsync(null);
            await UntilAsync(() => profiles.Loads > 0 && model.Audio.Inputs.Count > 0);

            Assert.False(model.Consent.IsShown);
            Assert.True(window.Find<SplitView>("Shell")!.IsEffectivelyEnabled);
            Assert.Equal(LegalTerms.Version, settings.Saved?.Consent?.Version);
            Assert.StartsWith($"Version {LegalTerms.Version}, accepted on ", model.Consent.AcceptedSummary, StringComparison.Ordinal);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Quit_closes_the_app_and_stores_nothing()
    {
        var (host, window, settings, profiles, _) = Open(AppSettings.Default);
        using (host)
        {
            var model = host.Services.GetRequiredService<MainViewModel>();
            await UntilAsync(() => model.Consent.IsShown);
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Find<Button>("ConsentDismiss")!.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(closed);
            Assert.Null(settings.Saved);
            Assert.Equal(0, profiles.Loads);
        }
    }

    [AvaloniaFact]
    public async Task Accepted_terms_start_the_app_without_the_screen()
    {
        var (host, window, _, profiles, _) = Open(new AppSettings(Consent: TestHost.Accepted));
        using (host)
        {
            var model = host.Services.GetRequiredService<MainViewModel>();
            await UntilAsync(() => profiles.Loads > 0);

            Assert.False(model.Consent.IsShown);
            Assert.True(model.Consent.Accepted.IsCompletedSuccessfully);
            Assert.False(window.Find<ConsentView>("ConsentOverlay")!.IsVisible);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_newer_bundled_version_asks_again()
    {
        var (host, window, settings, profiles, _) = Open(new AppSettings(KeepInTray: true, Consent: new("2026-01-01", DateTimeOffset.UnixEpoch)));
        using (host)
        {
            var model = host.Services.GetRequiredService<MainViewModel>();
            await UntilAsync(() => model.Consent.IsShown);

            Assert.Equal("The Terms of Service have changed", model.Consent.Heading);
            Assert.Equal(0, profiles.Loads);

            model.Consent.IsAccepted = true;
            await model.Consent.ContinueCommand.ExecuteAsync(null);

            Assert.Equal(LegalTerms.Version, settings.Saved?.Consent?.Version);
            Assert.True(settings.Saved?.KeepInTray);
            await UntilAsync(() => profiles.Loads > 0 && model.Audio.Inputs.Count > 0);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_newer_version_from_the_account_asks_again_and_says_to_accept_it_on_the_web()
    {
        var api = new FakeDesktopApi { Terms = new("2099-01-01", false) };
        var (host, window, settings, _, _) = Open(new AppSettings(Consent: TestHost.Accepted), api);
        using (host)
        {
            var model = host.Services.GetRequiredService<MainViewModel>();
            await UntilAsync(() => model.Consent.Accepted.IsCompleted && !model.Account.IsBusy);

            await model.Account.SignInCommand.ExecuteAsync(null);

            Assert.True(model.Consent.IsShown);
            Assert.True(model.Consent.IsBlocking);
            Assert.Equal("2099-01-01", model.Consent.Version);
            Assert.Contains("accept them", model.Account.Status, StringComparison.Ordinal);

            model.Consent.IsAccepted = true;
            await model.Consent.ContinueCommand.ExecuteAsync(null);
            Assert.Equal("2099-01-01", settings.Saved?.Consent?.Version);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task An_account_server_without_terms_keeps_the_bundled_version()
    {
        var api = new FakeDesktopApi { Terms = null };
        var (host, window, _, _, _) = Open(new AppSettings(Consent: TestHost.Accepted), api);
        using (host)
        {
            var model = host.Services.GetRequiredService<MainViewModel>();
            await UntilAsync(() => model.Consent.Accepted.IsCompleted && !model.Account.IsBusy);

            await model.Account.SignInCommand.ExecuteAsync(null);

            Assert.False(model.Consent.IsShown);
            Assert.Equal(LegalTerms.Version, model.Consent.Version);
            Assert.Equal("Signed in.", model.Account.Status);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Links_open_the_web_pages_and_the_licenses_show_the_bundled_notices()
    {
        var (host, window, _, _, browser) = Open(AppSettings.Default);
        using (host)
        {
            var model = host.Services.GetRequiredService<MainViewModel>();
            await UntilAsync(() => model.Consent.IsShown);

            window.Find<Button>("ConsentTermsLink")!.Command!.Execute(null);
            window.Find<Button>("ConsentPrivacyLink")!.Command!.Execute(null);
            window.Find<Button>("ConsentLicensesLink")!.Command!.Execute(null);
            window.UpdateLayout();

            Assert.Equal([new Uri("https://app.tropicastradio.com/terms"), new Uri("https://app.tropicastradio.com/privacy")], browser.Opened);
            var licenses = window.Find<SelectableTextBlock>("ConsentLicensesText")!;
            Assert.True(licenses.IsEffectivelyVisible);
            Assert.Contains("FFmpeg", licenses.Text, StringComparison.Ordinal);
            Assert.Contains("OpenSSL", licenses.Text, StringComparison.Ordinal);
            Assert.False(window.Find<StackPanel>("ConsentSummary")!.IsVisible);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task About_reviews_the_terms_and_close_goes_back_without_quitting()
    {
        var (host, window, settings, _, _) = Open(new AppSettings(Consent: TestHost.Accepted));
        using (host)
        {
            var model = host.Services.GetRequiredService<MainViewModel>();
            await UntilAsync(() => model.Consent.Accepted.IsCompleted);
            model.SelectedPage = AppPage.About;
            window.UpdateLayout();
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Find<Button>("ReviewTermsButton")!.Command!.Execute(null);
            Assert.True(model.Consent.IsShown);
            Assert.False(model.Consent.IsBlocking);
            Assert.True(model.Consent.IsAccepted);
            Assert.Equal("Close", model.Consent.DismissLabel);

            window.Find<Button>("ConsentDismiss")!.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.Consent.IsShown);
            Assert.False(closed);
            Assert.Null(settings.Saved);

            window.Find<Button>("LicensesButton")!.Command!.Execute(null);
            Assert.True(model.Consent.IsShown);
            Assert.True(model.Consent.ShowsLicenses);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Keyboard_starts_at_the_heading_and_tab_stays_on_the_screen()
    {
        var (host, window, settings, _, _) = Open(AppSettings.Default, width: 640, height: 420);
        using (host)
        {
            var model = host.Services.GetRequiredService<MainViewModel>();
            await UntilAsync(() => model.Consent.IsShown);
            var overlay = window.Find<ConsentView>("ConsentOverlay")!;
            await UntilAsync(() => Focused(window) is TextBlock { Name: "ConsentHeading" });

            // Tab to the checkbox and tick it with Space, then Tab on to Continue and press Enter.
            TabTo<CheckBox>(window, overlay, "ConsentAccept");
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(model.Consent.IsAccepted);
            TabTo<Button>(window, overlay, "ConsentContinue");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            await UntilAsync(() => !model.Consent.IsShown && model.Audio.Inputs.Count > 0);

            Assert.Equal(LegalTerms.Version, settings.Saved?.Consent?.Version);
            window.Close();
        }
    }

    /// <summary>Presses Tab until <paramref name="name"/> has the focus, which never leaves the screen on the way.</summary>
    private static void TabTo<T>(MainWindow window, ConsentView overlay, string name) where T : Control
    {
        for (var i = 0; i < 12 && (Focused(window) as T)?.Name != name; i++)
        {
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.Contains(overlay, (Focused(window) as Control)?.GetLogicalAncestors() ?? []);
        }
        Assert.Equal(name, (Focused(window) as T)?.Name);
    }

    private static object? Focused(MainWindow window) => window.FocusManager?.GetFocusedElement();

    private static (IHost Host, MainWindow Window, MemoryAppSettings Settings, CountingProfiles Profiles, Browser Browser) Open(
        AppSettings saved, FakeDesktopApi? api = null, double width = 980, double height = 720)
    {
        var settings = new MemoryAppSettings { Settings = saved };
        var profiles = new CountingProfiles();
        var browser = new Browser();
        var secrets = new MemorySecrets();
        var host = AppHost.Create([], services =>
        {
            services.AddSingleton(settings);
            services.AddSingleton<IAppSettingsStore>(settings);
            services.AddSingleton<IProfileStore>(profiles);
            services.AddSingleton<ISecretStore>(secrets);
            services.AddSingleton<IAudioCaptureProvider, ToneAudioCaptureProvider>();
            services.AddSingleton<IExternalBrowser>(browser);
            services.AddSingleton(new AccountService(api ?? new FakeDesktopApi(), new MemoryAccountStore(), secrets, new FastTime()));
        });
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Width = width;
        window.Height = height;
        window.Show();
        return (host, window, settings, profiles, browser);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, deadline.Token);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class CountingProfiles : IProfileStore
    {
        private readonly MemoryProfiles _inner = new();
        internal int Loads { get; private set; }

        public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
        {
            Loads++;
            return _inner.LoadAsync(cancellationToken);
        }

        public Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken = default)
            => _inner.SaveAsync(profiles, cancellationToken);
    }

    private sealed class Browser : IExternalBrowser
    {
        internal List<Uri> Opened { get; } = [];

        public void Open(Uri url) => Opened.Add(url);
    }
}
