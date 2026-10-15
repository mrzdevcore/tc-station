using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tropicast.Station.App.ViewModels;
using Tropicast.Station.App.Views;
using Tropicast.Station.Audio;
using Tropicast.Station.Audio.Linux;
using Tropicast.Station.Audio.MacOS;
using Tropicast.Station.Audio.Windows;
using Tropicast.Station.Core;
using Tropicast.Station.Encoding;
using Tropicast.Station.Infrastructure;

namespace Tropicast.Station.App;

/// <summary>Composition root: configuration, logging and dependency injection.</summary>
internal static class AppHost
{
    public static IHost Create(string[] args, Action<IServiceCollection>? configureServices = null)
    {
        var demoAudio = args.Contains("--demo-audio", StringComparer.Ordinal);
        var builder = Host.CreateApplicationBuilder(args.Where(a => a != "--demo-audio").ToArray());
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_ => new SafeLogProvider(SafeLogProvider.DefaultDirectory, Console.Error));
        builder.Services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<SafeLogProvider>());
        if (OperatingSystem.IsWindows() && !demoAudio)
        {
            builder.Services.AddWindowsAudioCapture();
        }
        else if (OperatingSystem.IsLinux() && !demoAudio)
        {
            builder.Services.AddLinuxAudioCapture();
        }
        else if (OperatingSystem.IsMacOS() && !demoAudio)
        {
            builder.Services.AddMacAudioCapture();
        }

        var apiBaseUrl = BaseUrl(builder.Configuration["Tropicast:ApiBaseUrl"], "Tropicast:ApiBaseUrl");
        builder.Services
            .AddStationCore()
            .AddStationAudio(demoAudio)
            .AddStationEncoding()
            .AddStationInfrastructure(apiBaseUrl);
        // The web app serves /terms and /privacy; in production it shares the API's address.
        builder.Services.AddSingleton(LegalLinks.For(BaseUrl(builder.Configuration["Tropicast:WebBaseUrl"], "Tropicast:WebBaseUrl")
            ?? apiBaseUrl ?? Infrastructure.HttpDesktopApi.DefaultBaseUrl));

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<AccountViewModel>();
        builder.Services.AddSingleton<IExternalBrowser, ExternalBrowser>();
        builder.Services.AddSingleton<ProfileEditorViewModel>();
        builder.Services.AddSingleton<AudioDevicesViewModel>();
        builder.Services.AddSingleton<AudioLevelsViewModel>();
        builder.Services.AddSingleton<BroadcastViewModel>();
        builder.Services.AddSingleton<GeneralSettingsViewModel>();
        builder.Services.AddSingleton<ConsentViewModel>();
        builder.Services.AddSingleton<ITraySupport, TraySupport>();
        builder.Services.AddSingleton<ITrayNotice, TrayNotice>();
        builder.Services.AddSingleton<IBroadcastConfirmation, BroadcastConfirmation>();
        builder.Services.AddSingleton<Diagnostics>();
        builder.Services.AddSingleton<UnhandledErrors>();
        builder.Services.AddTransient(sp =>
        {
            var window = new MainWindow
            {
                DataContext = sp.GetRequiredService<MainViewModel>(),
                Diagnostics = sp.GetRequiredService<Diagnostics>(),
                TrayNotice = sp.GetRequiredService<ITrayNotice>(),
            };
            if (sp.GetRequiredService<IBroadcastConfirmation>() is BroadcastConfirmation confirmation)
            {
                confirmation.Owner = window;
            }
            return window;
        });
        configureServices?.Invoke(builder.Services);

        return builder.Build();
    }

    /// <summary>
    /// A Tropicast address setting, null when unset (production then). <c>Tropicast:ApiBaseUrl</c> is the API (e.g. the
    /// environment variable <c>Tropicast__ApiBaseUrl=http://localhost:8080</c>); <c>Tropicast:WebBaseUrl</c> the web app
    /// with the Terms and Privacy pages, when it is not at the API's address (e.g. <c>http://localhost:5173</c>).
    /// </summary>
    private static Uri? BaseUrl(string? setting, string name)
        => string.IsNullOrWhiteSpace(setting) ? null
            : Uri.TryCreate(setting, UriKind.Absolute, out var url) && Infrastructure.HttpDesktopApi.IsAllowed(url) ? url
            : throw new InvalidOperationException($"{name} must be an HTTPS address (or HTTP on this computer).");
}
