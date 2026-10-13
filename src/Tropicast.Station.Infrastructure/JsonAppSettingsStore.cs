using System.Text.Json;
using Tropicast.Station.Core.Settings;

namespace Tropicast.Station.Infrastructure;

/// <summary>App preferences in <c>settings.json</c>. A missing or unreadable file means the defaults, never a failure.</summary>
public sealed class JsonAppSettingsStore(string directory) : IAppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(directory, "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var input = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(input, Options, cancellationToken).ConfigureAwait(false)
                ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(directory);
        // Write a temporary file, then replace: a crash never leaves a half-written settings file.
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                await JsonSerializer.SerializeAsync(output, settings, Options, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
