using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using System.ComponentModel;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Tropicast.Station.App.Controls;
using Tropicast.Station.App.ViewModels;

namespace Tropicast.Station.App.Views;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _checkingClose;
    private WindowNotificationManager? _notifications;
    internal Diagnostics? Diagnostics { get; set; }

    /// <summary>The tray icon, set by the app once created. Without one the window never hides.</summary>
    internal BroadcastTray? Tray { get; set; }

    /// <summary>Shown the first time the window hides to the tray.</summary>
    internal ITrayNotice? TrayNotice { get; set; }

    private bool _quitRequested;
    private bool _hiding;
    private bool _started;
    private long? _deactivatedAt;
    private WindowState _restoreState = WindowState.Normal;

    private bool HidesToTray => Tray is not null && DataContext is MainViewModel { Settings.KeepInTray: true };
    /// <summary>Below this window width the sidebar collapses to icons and opens over the page.</summary>
    private const double CompactWidth = 760;

    public MainWindow() => InitializeComponent();

    private SearchPage[]? _searchIndex;
    private bool _searchQueued;

    private IEnumerable<(AppPage Page, Control View)> PageViews =>
    [
        (AppPage.Broadcast, BroadcastPage), (AppPage.AudioSource, AudioSourcePage), (AppPage.Levels, LevelsPage),
        (AppPage.General, GeneralPage), (AppPage.Account, AccountPage), (AppPage.Profiles, ProfilesPage), (AppPage.Diagnostics, DiagnosticsPage),
        (AppPage.About, AboutPage),
    ];

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var compact = e.NewSize.Width < CompactWidth;
        if (compact == (Shell.DisplayMode == SplitViewDisplayMode.CompactOverlay))
        {
            return;
        }
        Shell.DisplayMode = compact ? SplitViewDisplayMode.CompactOverlay : SplitViewDisplayMode.Inline;
        Shell.IsPaneOpen = !compact;
        PaneToggle.IsVisible = compact;
    }

    private void OnTogglePane(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Shell.IsPaneOpen = !Shell.IsPaneOpen;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Shell.IsPaneOpen = true;
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Shell.DisplayMode == SplitViewDisplayMode.CompactOverlay && Shell.IsPaneOpen)
        {
            Shell.IsPaneOpen = false;
            e.Handled = true;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel model)
        {
            return;
        }
        if (e.PropertyName == nameof(MainViewModel.SearchText))
        {
            ApplySearch(model);
        }
        // A page picked in the overlaid sidebar closes it; a page the search moves to does not, so typing goes on.
        else if (e.PropertyName == nameof(MainViewModel.SelectedPage) && Shell.DisplayMode == SplitViewDisplayMode.CompactOverlay
                 && !SearchBox.IsKeyboardFocusWithin)
        {
            Shell.IsPaneOpen = false;
        }
    }

    /// <summary>What the search filters on one page: its rows, its groups (heading, notes and card) and its intro text.</summary>
    private sealed record SearchPage(AppPage Page, SettingRow[] Rows, SearchGroup[] Groups, TextBlock[] Intros);

    /// <summary>A "group" panel; a "keep" group (level meters, the profile actions) stays whenever its page shows.</summary>
    private sealed record SearchGroup(Panel Panel, SettingRow[] Rows, bool Keep);

    private SearchPage[] SearchIndex => _searchIndex ??= BuildSearchIndex();

    private SearchPage[] BuildSearchIndex()
    {
        var index = PageViews.Select(entry =>
        {
            var controls = entry.View.GetLogicalDescendants().ToList();
            var groups = controls.OfType<Panel>().Where(panel => panel.Classes.Contains("group"))
                .Select(panel => new SearchGroup(panel, panel.GetLogicalDescendants().OfType<SettingRow>().ToArray(),
                    panel.Classes.Contains("keep")))
                .ToArray();
            return new SearchPage(entry.Page, controls.OfType<SettingRow>().ToArray(), groups,
                controls.OfType<TextBlock>().Where(text => text.Classes.Contains("pageIntro")).ToArray());
        }).ToArray();
        // Rows appear, disappear and change text with the app state (sign-in, toggles, status lines): search again then.
        foreach (var row in index.SelectMany(page => page.Rows))
        {
            row.PropertyChanged += OnRowChanged;
        }
        return index;
    }

    private void OnRowChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if ((e.Property != IsVisibleProperty && e.Property != SettingRow.TitleProperty && e.Property != SettingRow.DescriptionProperty)
            || _searchQueued || DataContext is not MainViewModel { SearchText: var text } || text.Trim().Length == 0)
        {
            return;
        }
        _searchQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _searchQueued = false;
            if (DataContext is MainViewModel model)
            {
                ApplySearch(model);
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Keeps the shown rows whose title or description match, the groups that still have one, and the pages
    /// that still have one (or whose name matches: then the whole page shows).
    /// </summary>
    private void ApplySearch(MainViewModel model)
    {
        var query = model.SearchText.Trim();
        var searching = query.Length > 0;
        var pages = new HashSet<AppPage>();
        foreach (var page in SearchIndex)
        {
            var wholePage = !searching || MainViewModel.PageLabel(page.Page).Contains(query, StringComparison.CurrentCultureIgnoreCase);
            var any = false;
            foreach (var row in page.Rows)
            {
                // A row its page hides (Opus bitrate with Opus off, sign-in steps) is no result.
                var hit = row.IsVisible && row.Matches(query);
                row.IsFilteredOut = !wholePage && !hit;
                any |= hit;
            }
            foreach (var group in page.Groups)
            {
                var empty = !group.Rows.Any(row => row.IsVisible && !row.IsFilteredOut);
                group.Panel.Classes.Set("filtered", !wholePage && !group.Keep && empty);
            }
            foreach (var intro in page.Intros)
            {
                intro.Classes.Set("filtered", !wholePage);
            }
            if (wholePage || any)
            {
                pages.Add(page.Page);
            }
        }
        model.ShowPages(pages);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Opened fires again each time the window comes back from the tray: start up once.
        if (_started)
        {
            return;
        }
        _started = true;
        DiagnosticsPage.Diagnostics = Diagnostics;
        Deactivated += (_, _) => NoteDeactivated();
        if (DataContext is MainViewModel model)
        {
            model.PropertyChanged += OnModelChanged;
            _notifications = new(this) { Position = NotificationPosition.TopRight, MaxItems = 1 };
            model.Audio.Levels.SilenceStarted += OnSilenceStarted;
            model.Settings.LoadCommand.Execute(null);
            model.Account.LoadCommand.Execute(null);
            model.Profiles.LoadCommand.Execute(null);
            model.Audio.RefreshCommand.Execute(null);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel model)
        {
            model.Audio.Levels.SilenceStarted -= OnSilenceStarted;
            model.PropertyChanged -= OnModelChanged;
            model.Account.SignInCommand.Cancel();
            model.Profiles.TestCommand.Cancel();
            model.Profiles.Password = "";
            if (model.Audio.StopCommand.CanExecute(null))
            {
                model.Audio.StopCommand.Execute(null);
            }
        }

        base.OnClosed(e);
    }

    private void OnSilenceStarted(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel model)
        {
            _notifications?.Show(new Notification("Audio silence", model.Audio.Levels.Status,
                NotificationType.Warning, TimeSpan.FromSeconds(5)));
        }
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty)
        {
            return;
        }
        if ((WindowState)change.NewValue! is WindowState.Minimized)
        {
            if (HidesToTray)
            {
                _ = HideToTrayAsync();
            }
        }
        else
        {
            // What "Open" restores: normal, maximized or full screen as the window last was.
            _restoreState = (WindowState)change.NewValue!;
        }
    }

    /// <summary>Hides the window (and its taskbar or Dock entry); the app and any broadcast keep running.</summary>
    internal async Task HideToTrayAsync()
    {
        if (_hiding)
        {
            return;
        }
        _hiding = true;
        try
        {
            if (DataContext is MainViewModel model && !model.Settings.TrayNoticeShown && TrayNotice is not null)
            {
                await model.Settings.MarkTrayNoticeShownAsync();
                // The notice needs the window on screen: bring a minimized window back first.
                WindowState = _restoreState;
                await TrayNotice.ShowAsync(this);
            }
            Hide();
        }
        finally
        {
            _hiding = false;
        }
    }

    /// <summary>Tray "Open" (as the window last was) and "Maximize window".</summary>
    internal void ShowFromTray(bool maximize = false)
    {
        Show();
        WindowState = maximize ? WindowState.Maximized : _restoreState;
        Activate();
    }

    /// <summary>The window lost focus, for example to a click on the tray icon.</summary>
    internal void NoteDeactivated() => _deactivatedAt = Environment.TickCount64;

    /// <summary>Tray left click: brings the window to the front, or hides it when it already is.</summary>
    internal void ToggleFromTray()
    {
        // Clicking a Windows tray icon takes focus from the window just before the click arrives:
        // a window that was active a moment ago counts as in front.
        var inFront = IsActive || (_deactivatedAt is { } at && Environment.TickCount64 - at < 500);
        if (IsVisible && inFront && WindowState != WindowState.Minimized)
        {
            _ = HideToTrayAsync();
        }
        else
        {
            ShowFromTray();
        }
    }

    /// <summary>Tray "Quit": closes for real, after the live-broadcast confirmation (shown on the window).</summary>
    internal void Quit()
    {
        if (!IsVisible)
        {
            ShowFromTray();
        }
        _quitRequested = true;
        Close();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        var quitting = _quitRequested || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
        if (!quitting && !_closeApproved && HidesToTray)
        {
            e.Cancel = true;
            base.OnClosing(e);
            await HideToTrayAsync();
            return;
        }
        if (!_closeApproved && DataContext is MainViewModel model && model.Broadcast.HasActiveBroadcast)
        {
            e.Cancel = true;
            base.OnClosing(e);
            if (_checkingClose)
            {
                return;
            }
            _checkingClose = true;
            try
            {
                if (await model.Broadcast.RequestCloseAsync())
                {
                    _closeApproved = true;
                    Close();
                }
                else
                {
                    _quitRequested = false;
                }
            }
            finally
            {
                _checkingClose = false;
            }
            return;
        }
        base.OnClosing(e);
    }
}
