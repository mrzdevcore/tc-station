using Microsoft.Extensions.DependencyInjection;
using Tropicast.Station.Core.Account;
using Tropicast.Station.Core.Broadcasting;
using Tropicast.Station.Core.Profiles;
using Tropicast.Station.Core.Settings;

namespace Tropicast.Station.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <param name="services">The services.</param>
    /// <param name="apiBaseUrl">Tropicast API address (setting <c>Tropicast:ApiBaseUrl</c>); production when null.</param>
    public static IServiceCollection AddStationInfrastructure(this IServiceCollection services, Uri? apiBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tropicast", "Station");
        services.AddSingleton<IProfileStore>(_ => new JsonProfileStore(directory));
        services.AddSingleton<IAccountStore>(_ => new JsonAccountStore(directory));
        services.AddSingleton<IAppSettingsStore>(_ => new JsonAppSettingsStore(directory));
        services.AddSingleton<IDesktopApi>(_ => new HttpDesktopApi(apiBaseUrl ?? HttpDesktopApi.DefaultBaseUrl));
        services.AddSingleton<ISecretStore, OsSecretStore>();
        services.AddSingleton<IConnectionTester, TropicastConnectionTester>();
        return services;
    }
}
