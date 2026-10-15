using Tropicast.Station.Core.Settings;

namespace Tropicast.Station.Infrastructure.Tests;

public sealed class SettingsStorageTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Accepted_terms_survive_a_restart_and_older_files_still_load()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tc-settings-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonAppSettingsStore(directory);
            Directory.CreateDirectory(directory);
            // settings.json of a version before #68: no consent, so the first-run screen shows.
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), """{"KeepInTray":true,"TrayNoticeShown":true}""", Token);
            Assert.Equal(new AppSettings(KeepInTray: true, TrayNoticeShown: true), await store.LoadAsync(Token));

            var consent = new ConsentRecord("2026-10-14", new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero));
            await store.SaveAsync(new AppSettings(KeepInTray: true, TrayNoticeShown: true, Consent: consent), Token);

            Assert.Equal(consent, (await new JsonAppSettingsStore(directory).LoadAsync(Token)).Consent);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("2026-10-14", null, true)]
    [InlineData("2026-10-14", "2026-10-14", false)]
    [InlineData("2026-10-14", "2026-11-01", false)]
    [InlineData("2027-01-01", "2026-10-14", true)]
    public void A_version_is_newer_by_its_date(string version, string? accepted, bool newer)
        => Assert.Equal(newer, LegalTerms.IsNewer(version, accepted));

    [Theory]
    [InlineData("2026-10-14", true)]
    [InlineData("2026-13-01", false)]
    [InlineData("v2", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_dates_are_versions(string? version, bool valid) => Assert.Equal(valid, LegalTerms.IsValid(version));
}
