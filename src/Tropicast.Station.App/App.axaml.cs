using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tropicast.Station.App.Views;

namespace Tropicast.Station.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Avalonia owns the application; OnExit disposes the tray and host.")]
public partial class App : Application
{
    private static readonly TimeSpan HostShutdownTimeout = TimeSpan.FromSeconds(5);

    private IHost? _host;
    private BroadcastTray? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = AppHost.Create(desktop.Args ?? []);
            _host.Start();

            // The host traps SIGTERM/Ctrl+C; close the UI too so the process actually exits.
            _host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(
                () => Dispatcher.UIThread.Post(() => desktop.Shutdown()));

            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
            var errors = _host.Services.GetRequiredService<UnhandledErrors>();
            errors.Owner = desktop.MainWindow;
            errors.Shutdown = () => desktop.Shutdown(1);
            errors.Attach();
            var window = (MainWindow)desktop.MainWindow;
            _ = AddTrayAsync(window, _host.Services.GetRequiredService<ViewModels.MainViewModel>());
            // macOS: clicking the Dock icon of a window hidden to the menu bar brings it back.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, args) =>
                {
                    if (args.Kind == ActivationKind.Reopen)
                    {
                        window.ShowFromTray();
                    }
                };
            }
            desktop.Exit += OnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The tray icon (and its Go Live item) appears once the terms are accepted (#68).</summary>
    private async Task AddTrayAsync(MainWindow window, ViewModels.MainViewModel model)
    {
        await model.Consent.Accepted;
        if (_host is null)
        {
            return;
        }
        _tray = new BroadcastTray(window, model.Broadcast);
        window.Tray = _tray;
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        using var cts = new CancellationTokenSource(HostShutdownTimeout);
        _host.Services.GetRequiredService<UnhandledErrors>().Dispose();
        _tray?.Dispose();
        _tray = null;
        _host.StopAsync(cts.Token).GetAwaiter().GetResult();
        _host.Dispose();
        _host = null;
    }
}
