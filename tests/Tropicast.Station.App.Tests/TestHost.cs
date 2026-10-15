using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tropicast.Station.Core.Settings;

namespace Tropicast.Station.App.Tests;

/// <summary>
/// The app host with settings in memory (never the settings.json of the computer running the tests) and the current
/// terms already accepted, so the window starts as after the first run. ConsentTests start without them.
/// </summary>
internal static class TestHost
{
    public static readonly ConsentRecord Accepted = new(LegalTerms.Version, new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));

    public static IHost Create(string[] args, Action<IServiceCollection>? configureServices = null)
        => AppHost.Create(args, services =>
        {
            services.AddSingleton(new MemoryAppSettings { Settings = new AppSettings(Consent: Accepted) });
            services.AddSingleton<IAppSettingsStore>(sp => sp.GetRequiredService<MemoryAppSettings>());
            configureServices?.Invoke(services);
        });
}
