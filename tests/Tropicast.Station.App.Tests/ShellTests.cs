using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Station.App.Controls;
using Tropicast.Station.App.ViewModels;
using Tropicast.Station.App.Views;

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
        Assert.Equal(9, model.NavEntries.Count);
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
}
