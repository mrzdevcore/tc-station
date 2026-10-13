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

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Saved = Settings = settings;
        return Task.CompletedTask;
    }
}
