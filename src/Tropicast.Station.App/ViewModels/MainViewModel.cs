using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tropicast.Station.Core;

namespace Tropicast.Station.App.ViewModels;

/// <summary>The pages of the main window, in sidebar order.</summary>
public enum AppPage { Broadcast, AudioSource, Levels, General, Account, Profiles, Diagnostics, About }

/// <summary>A sidebar entry: a section caption (no page) or a page with its icon name.</summary>
public sealed record NavEntry(string Label, AppPage? Page = null, string? Icon = null)
{
    public bool IsCaption => Page is null;
    public bool IsSelectable => Page is not null;
}

public sealed partial class MainViewModel : ViewModelBase
{
    private static readonly NavEntry[] AllEntries =
    [
        new("Station"),
        new("Broadcast", AppPage.Broadcast, "Broadcast"),
        new("Audio source", AppPage.AudioSource, "Microphone"),
        new("Levels & silence", AppPage.Levels, "Levels"),
        new("Settings"),
        new("General", AppPage.General, "Settings"),
        new("Account", AppPage.Account, "User"),
        new("Connection profiles", AppPage.Profiles, "Server"),
        new("Diagnostics", AppPage.Diagnostics, "Diagnostics"),
        new("About", AppPage.About, "Info"),
    ];

    public MainViewModel(IAppInfo appInfo, AccountViewModel account, ProfileEditorViewModel profiles, AudioDevicesViewModel audio,
        BroadcastViewModel broadcast, GeneralSettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(appInfo);
        Title = appInfo.ProductName;
        Version = $"v{appInfo.Version}";
        Account = account;
        Profiles = profiles;
        Audio = audio;
        Broadcast = broadcast;
        Settings = settings;
        NavEntries = new(AllEntries);
        SelectedNav = AllEntries[1];
    }

    public string Title { get; }

    public string Version { get; }

    public AccountViewModel Account { get; }
    public ProfileEditorViewModel Profiles { get; }
    public AudioDevicesViewModel Audio { get; }
    public BroadcastViewModel Broadcast { get; }
    public GeneralSettingsViewModel Settings { get; }

    /// <summary>Sidebar entries left by the settings search, captions included.</summary>
    public ObservableCollection<NavEntry> NavEntries { get; }

    [ObservableProperty] public partial AppPage SelectedPage { get; set; } = AppPage.Broadcast;
    [ObservableProperty] public partial NavEntry? SelectedNav { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial bool HasNoSearchResults { get; set; }

    partial void OnSelectedNavChanged(NavEntry? value)
    {
        if (value?.Page is { } page)
        {
            SelectedPage = page;
        }
    }

    partial void OnSelectedPageChanged(AppPage value)
    {
        if (SelectedNav?.Page != value)
        {
            SelectedNav = NavEntries.FirstOrDefault(entry => entry.Page == value);
        }
    }

    [RelayCommand]
    private void Navigate(AppPage page) => SelectedPage = page;

    /// <summary>Keeps the sidebar entries of <paramref name="pages"/> (and their captions); moves off a page that is gone.</summary>
    public void ShowPages(IReadOnlySet<AppPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        NavEntries.Clear();
        for (var i = 0; i < AllEntries.Length; i++)
        {
            var entry = AllEntries[i];
            var keep = entry.Page is { } page
                ? pages.Contains(page)
                : AllEntries.Skip(i + 1).TakeWhile(next => !next.IsCaption).Any(next => pages.Contains(next.Page!.Value));
            if (keep)
            {
                NavEntries.Add(entry);
            }
        }
        HasNoSearchResults = pages.Count == 0;
        if (!pages.Contains(SelectedPage) && NavEntries.FirstOrDefault(entry => entry.IsSelectable) is { } first)
        {
            SelectedPage = first.Page!.Value;
        }
        SelectedNav = NavEntries.FirstOrDefault(entry => entry.Page == SelectedPage);
    }

    /// <summary>The sidebar label of <paramref name="page"/>.</summary>
    public static string PageLabel(AppPage page) => AllEntries.First(entry => entry.Page == page).Label;
}
