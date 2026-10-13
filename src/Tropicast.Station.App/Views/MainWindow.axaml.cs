using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using System.ComponentModel;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Tropicast.Station.App.Controls;
using Tropicast.Station.App.ViewModels;

namespace Tropicast.Station.App.Views;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _checkingClose;
    private WindowNotificationManager? _notifications;
    internal Diagnostics? Diagnostics { get; set; }
    /// <summary>Below this window width the sidebar collapses to icons and opens over the page.</summary>
    private const double CompactWidth = 760;

    public MainWindow() => InitializeComponent();

    private IEnumerable<(AppPage Page, Control View)> Pages =>
    [
        (AppPage.Broadcast, BroadcastPage), (AppPage.AudioSource, AudioSourcePage), (AppPage.Levels, LevelsPage),
        (AppPage.Account, AccountPage), (AppPage.Profiles, ProfilesPage), (AppPage.Diagnostics, DiagnosticsPage),
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
        else if (e.PropertyName == nameof(MainViewModel.SelectedPage) && Shell.DisplayMode == SplitViewDisplayMode.CompactOverlay)
        {
            Shell.IsPaneOpen = false;
        }
    }

    /// <summary>
    /// Keeps the rows whose title or description match the search, and the pages that still have one
    /// (or whose name matches: then the whole page shows).
    /// </summary>
    private void ApplySearch(MainViewModel model)
    {
        var query = model.SearchText.Trim();
        var pages = new HashSet<AppPage>();
        foreach (var (page, view) in Pages)
        {
            var rows = view.GetLogicalDescendants().OfType<SettingRow>().ToList();
            var wholePage = query.Length == 0 || MainViewModel.PageLabel(page).Contains(query, StringComparison.CurrentCultureIgnoreCase);
            var any = wholePage;
            foreach (var row in rows)
            {
                var hit = wholePage || row.Matches(query);
                row.IsFilteredOut = !hit;
                any |= hit;
            }
            // Cards without a match (and their heading) go too.
            foreach (var card in view.GetLogicalDescendants().OfType<SettingsCard>())
            {
                var empty = !card.Children.OfType<SettingRow>().Any(row => !row.IsFilteredOut) && !wholePage;
                card.Classes.Set("filtered", empty);
                if (card.Parent is Panel panel && panel.Children.IndexOf(card) is > 0 and var index
                    && panel.Children[index - 1] is TextBlock heading && heading.Classes.Contains("cardTitle"))
                {
                    heading.Classes.Set("filtered", empty);
                }
            }
            if (any)
            {
                pages.Add(page);
            }
        }
        model.ShowPages(pages);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        DiagnosticsPage.Diagnostics = Diagnostics;
        if (DataContext is MainViewModel model)
        {
            model.PropertyChanged += OnModelChanged;
            _notifications = new(this) { Position = NotificationPosition.TopRight, MaxItems = 1 };
            model.Audio.Levels.SilenceStarted += OnSilenceStarted;
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

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
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
