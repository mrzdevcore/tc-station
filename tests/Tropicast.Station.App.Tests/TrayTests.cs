using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tropicast.Station.App.ViewModels;
using Tropicast.Station.App.Views;
using Tropicast.Station.Core.Settings;

namespace Tropicast.Station.App.Tests;

public sealed class TrayTests
{
    [AvaloniaFact]
    public async Task Close_hides_to_the_tray_and_open_restores_the_window()
    {
        var (host, window, tray, _) = await OpenAsync(new AppSettings(KeepInTray: true, TrayNoticeShown: true));
        using (host)
        using (tray)
        {
            window.WindowState = WindowState.Maximized;
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
            Assert.False(closed);

            tray.Item("Open Tropicast Station").Command!.Execute(null);
            Assert.True(window.IsVisible);
            Assert.Equal(WindowState.Maximized, window.WindowState);

            tray.Item("Quit Tropicast Station").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(closed);
        }
    }

    [AvaloniaFact]
    public async Task Minimize_hides_to_the_tray_and_maximize_brings_it_back_maximized()
    {
        var (host, window, tray, _) = await OpenAsync(new AppSettings(KeepInTray: true, TrayNoticeShown: true));
        using (host)
        using (tray)
        {
            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);

            tray.Item("Maximize window").Command!.Execute(null);
            Assert.True(window.IsVisible);
            Assert.Equal(WindowState.Maximized, window.WindowState);
            window.Quit();
        }
    }

    [AvaloniaFact]
    public async Task First_hide_shows_the_notice_once_and_remembers_it()
    {
        var (host, window, tray, notice) = await OpenAsync(new AppSettings(KeepInTray: true));
        using (host)
        using (tray)
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            window.ShowFromTray();
            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, notice.Count);
            Assert.False(window.IsVisible);
            Assert.True(host.Services.GetRequiredService<MemoryAppSettings>().Saved?.TrayNoticeShown);
            window.Quit();
        }
    }

    [AvaloniaFact]
    public async Task With_the_setting_off_close_quits_as_before()
    {
        var (host, window, tray, _) = await OpenAsync(new AppSettings(KeepInTray: false));
        using (host)
        using (tray)
        {
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(closed);
        }
    }

    [AvaloniaFact]
    public async Task Default_follows_tray_detection_and_the_choice_is_saved()
    {
        var (host, window, tray, _) = await OpenAsync(AppSettings.Default, trayDetected: false);
        using (host)
        using (tray)
        {
            var settings = host.Services.GetRequiredService<MainViewModel>().Settings;
            Assert.False(settings.KeepInTray);
            Assert.Contains("No system tray", settings.TrayHint, StringComparison.Ordinal);

            settings.KeepInTray = true;
            Assert.True(host.Services.GetRequiredService<MemoryAppSettings>().Saved?.KeepInTray);
            window.Quit();
        }
    }

    [AvaloniaFact]
    public async Task Tray_menu_lists_open_maximize_broadcast_and_quit()
    {
        var (host, window, tray, _) = await OpenAsync(new AppSettings(KeepInTray: true, TrayNoticeShown: true));
        using (host)
        using (tray)
        {
            var headers = tray.Menu.Items.OfType<NativeMenuItem>().Where(item => item is not NativeMenuItemSeparator)
                .Select(item => item.Header).ToList();
            Assert.Equal(["Idle — 00:00:00", "Open Tropicast Station", "Maximize window", "Go Live", "Stop broadcast", "Quit Tropicast Station"],
                headers);
            Assert.False(tray.Item("Go Live").IsEnabled);
            Assert.False(tray.Item("Stop broadcast").IsEnabled);
            window.Quit();
        }
    }

    [AvaloniaFact]
    public async Task Coming_back_from_the_tray_does_not_start_up_again()
    {
        var (host, window, tray, _) = await OpenAsync(new AppSettings(KeepInTray: true, TrayNoticeShown: true));
        using (host)
        using (tray)
        {
            var store = host.Services.GetRequiredService<MemoryAppSettings>();
            var loads = store.Loads;
            for (var i = 0; i < 3; i++)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
                window.ShowFromTray();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(loads, store.Loads);
            window.Quit();
        }
    }

    [AvaloniaFact]
    public async Task Tray_click_hides_a_window_in_front_and_shows_a_hidden_one()
    {
        var (host, window, tray, _) = await OpenAsync(new AppSettings(KeepInTray: true, TrayNoticeShown: true));
        using (host)
        using (tray)
        {
            // On Windows the click on the tray icon takes focus from the window just before it arrives.
            window.NoteDeactivated();
            window.ToggleFromTray();
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
            window.ToggleFromTray();
            Assert.True(window.IsVisible);
            window.Quit();
        }
    }

    [AvaloniaFact]
    public async Task Saved_choice_applies_without_waiting_for_the_tray_check()
    {
        var detection = new TaskCompletionSource<bool>();
        var settings = new GeneralSettingsViewModel(new MemoryAppSettings { Settings = new(KeepInTray: true) }, new SlowDetector(detection.Task));

        var load = settings.LoadCommand.ExecuteAsync(null);

        Assert.True(settings.KeepInTray);
        detection.SetResult(false);
        await load;
        Assert.True(settings.KeepInTray);
        Assert.False(settings.TrayDetected);
    }

    [AvaloniaFact]
    public async Task A_choice_made_while_loading_wins_and_is_saved()
    {
        var detection = new TaskCompletionSource<bool>();
        var store = new MemoryAppSettings();
        var settings = new GeneralSettingsViewModel(store, new SlowDetector(detection.Task));

        var load = settings.LoadCommand.ExecuteAsync(null);
        settings.KeepInTray = true;
        detection.SetResult(false);
        await load;

        Assert.True(settings.KeepInTray);
        Assert.True(store.Saved?.KeepInTray);
    }

    [AvaloniaFact]
    public async Task Saves_run_one_at_a_time_and_the_last_choice_lands_last()
    {
        var store = new SlowStore();
        var settings = new GeneralSettingsViewModel(store, new SlowDetector(Task.FromResult(true)));
        await settings.LoadCommand.ExecuteAsync(null);

        settings.KeepInTray = false;
        settings.KeepInTray = true;
        settings.KeepInTray = false;
        await settings.MarkTrayNoticeShownAsync();

        Assert.Equal(1, store.MaxConcurrent);
        Assert.Equal(new AppSettings(KeepInTray: false, TrayNoticeShown: true), store.Last);
    }

    private static async Task<(IHost Host, MainWindow Window, BroadcastTray Tray, Notice Notice)> OpenAsync(
        AppSettings settings, bool trayDetected = true)
    {
        var notice = new Notice();
        var host = AppHost.Create(["--demo-audio"], services =>
        {
            services.AddSingleton(new MemoryAppSettings { Settings = settings });
            services.AddSingleton<IAppSettingsStore>(sp => sp.GetRequiredService<MemoryAppSettings>());
            services.AddSingleton<ITraySupport>(new Detector(trayDetected));
            services.AddSingleton<ITrayNotice>(notice);
        });
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        var model = host.Services.GetRequiredService<MainViewModel>();
        await model.Settings.LoadCommand.ExecuteAsync(null);
        var tray = new BroadcastTray(window, model.Broadcast);
        window.Tray = tray;
        return (host, window, tray, notice);
    }

    private sealed class SlowDetector(Task<bool> result) : ITraySupport
    {
        public Task<bool> IsAvailableAsync() => result;
    }

    private sealed class SlowStore : IAppSettingsStore
    {
        private int _running;
        internal int MaxConcurrent { get; private set; }
        internal AppSettings? Last { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(AppSettings.Default);

        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _running));
            await Task.Delay(10, cancellationToken);
            Last = settings;
            Interlocked.Decrement(ref _running);
        }
    }

    private sealed class Detector(bool available) : ITraySupport
    {
        public Task<bool> IsAvailableAsync() => Task.FromResult(available);
    }

    private sealed class Notice : ITrayNotice
    {
        internal int Count { get; private set; }

        public Task ShowAsync(Window owner)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}

internal sealed class MemoryAppSettings : IAppSettingsStore
{
    internal AppSettings Settings { get; set; } = AppSettings.Default;
    internal AppSettings? Saved { get; private set; }
    internal int Loads { get; private set; }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        Loads++;
        return Task.FromResult(Settings);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Saved = Settings = settings;
        return Task.CompletedTask;
    }
}
