using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Station.App.Controls;
using Tropicast.Station.App.ViewModels;
using Tropicast.Station.App.Views;
using Tropicast.Station.Core.Profiles;
using Tropicast.Station.Tests;

namespace Tropicast.Station.App.Tests;

public sealed class ShellTests
{
    [AvaloniaFact]
    public void Go_live_stays_reachable_from_every_page()
    {
        using var host = AppHost.Create(["--demo-audio"]);
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        var model = host.Services.GetRequiredService<MainViewModel>();

        foreach (var page in Enum.GetValues<AppPage>())
        {
            model.SelectedPage = page;
            window.UpdateLayout();
            Assert.Equal(page, model.SelectedNav?.Page);
            Assert.True(window.Find<Button>("GoLiveButton")!.IsEffectivelyVisible, page.ToString());
            Assert.True(window.Find<TextBlock>("BroadcastState")!.IsEffectivelyVisible, page.ToString());
        }
        window.Close();
    }

    [AvaloniaFact]
    public void Search_keeps_matching_pages_and_rows_and_says_when_nothing_matches()
    {
        using var host = AppHost.Create(["--demo-audio"]);
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        var model = host.Services.GetRequiredService<MainViewModel>();
        var rows = window.Find<Control>("ProfilesPage")!.GetLogicalDescendants().OfType<SettingRow>().ToList();

        model.SearchText = "bitrate";

        Assert.Equal(["Settings", "Connection profiles"], model.NavEntries.Select(entry => entry.Label));
        Assert.Equal(AppPage.Profiles, model.SelectedPage);
        Assert.False(rows.Single(row => row.Title == "Bitrate (kbps)").IsFilteredOut);
        Assert.True(rows.Single(row => row.Title == "Host").IsFilteredOut);

        model.SearchText = "no such setting";
        Assert.True(model.HasNoSearchResults);
        Assert.True(window.Find<StackPanel>("NoResults")!.IsVisible);

        model.SearchText = "";
        Assert.Equal(10, model.NavEntries.Count);
        Assert.False(model.HasNoSearchResults);
        Assert.DoesNotContain(rows, row => row.IsFilteredOut);
        window.Close();
    }

    [AvaloniaFact]
    public void Small_window_collapses_the_sidebar_to_icons()
    {
        using var host = AppHost.Create(["--demo-audio"]);
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Width = 640;
        window.Height = 420;
        window.Show();
        var shell = window.Find<SplitView>("Shell")!;

        Assert.Equal(SplitViewDisplayMode.CompactOverlay, shell.DisplayMode);
        Assert.False(shell.IsPaneOpen);
        Assert.True(window.Find<Button>("PaneToggle")!.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Default_window_shows_the_full_sidebar()
    {
        using var host = AppHost.Create(["--demo-audio"]);
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        var shell = window.Find<SplitView>("Shell")!;

        Assert.Equal(SplitViewDisplayMode.Inline, shell.DisplayMode);
        Assert.True(shell.IsPaneOpen);
        Assert.False(window.Find<Button>("PaneToggle")!.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Search_ignores_hidden_rows_and_follows_them_when_they_appear()
    {
        using var host = AppHost.Create(["--demo-audio"], services =>
        {
            services.AddSingleton<IProfileStore, MemoryProfiles>();
            services.AddSingleton<ISecretStore, MemorySecrets>();
        });
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        var model = host.Services.GetRequiredService<MainViewModel>();
        await model.Profiles.LoadCommand.ExecuteAsync(null);
        model.Profiles.PublishOpus = false;

        model.SearchText = "opus bitrate";
        Assert.True(model.HasNoSearchResults);
        Assert.False(window.Find<Panel>("PageHost")!.IsVisible);

        model.Profiles.PublishOpus = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(model.HasNoSearchResults);
        Assert.Equal(AppPage.Profiles, model.SelectedPage);
        Assert.True(window.Find<Panel>("PageHost")!.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Search_hides_whole_groups_but_keeps_meters_and_profile_actions()
    {
        using var host = AppHost.Create(["--demo-audio"]);
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        var model = host.Services.GetRequiredService<MainViewModel>();
        static Panel GroupOf(Control control) =>
            control.GetLogicalAncestors().OfType<Panel>().First(panel => panel.Classes.Contains("group"));

        model.SearchText = "bitrate";
        // The heading and the note of "Stream identification" go with its card.
        Assert.False(GroupOf(window.Find<TextBox>("StreamNameInput")!).IsVisible);
        Assert.True(GroupOf(window.Find<ComboBox>("ProfileBitrate")!).IsVisible);

        model.SearchText = "threshold";
        Assert.Equal(AppPage.Levels, model.SelectedPage);
        Assert.True(GroupOf(window.Find<ItemsControl>("ChannelMeters")!).IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Account_page_shows_the_sign_in_code_for_a_manual_profile_user_too()
    {
        using var host = AppHost.Create(["--demo-audio"]);
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        var model = host.Services.GetRequiredService<MainViewModel>();
        model.Broadcast.UseProfile = true;
        model.Account.UserCode = "BCDF-GHJK";
        model.Account.IsSigningIn = true;

        model.SelectedPage = AppPage.Account;
        window.UpdateLayout();

        var code = window.Find<TextBlock>("AccountUserCode")!;
        Assert.True(code.IsEffectivelyVisible);
        Assert.Equal("BCDF-GHJK", code.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Typing_a_search_in_a_small_window_keeps_the_sidebar_open()
    {
        using var host = AppHost.Create(["--demo-audio"]);
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Width = 640;
        window.Height = 420;
        window.Show();
        var model = host.Services.GetRequiredService<MainViewModel>();
        var shell = window.Find<SplitView>("Shell")!;
        shell.IsPaneOpen = true;
        window.UpdateLayout();
        Assert.True(window.Find<TextBox>("SearchBox")!.Focus());

        model.SearchText = "bitrate";

        Assert.Equal(AppPage.Profiles, model.SelectedPage);
        Assert.True(shell.IsPaneOpen);
        window.Close();
    }
}
