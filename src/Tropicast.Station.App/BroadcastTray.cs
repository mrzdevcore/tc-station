using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using Tropicast.Station.App.ViewModels;
using Tropicast.Station.App.Views;
using Tropicast.Station.Encoding;

namespace Tropicast.Station.App;

/// <summary>
/// The tray (Windows, Linux) or menu-bar (macOS) icon: live state, and a menu to open or maximize the window,
/// go live or stop, and quit. A left click brings the window to the front or hides it.
/// </summary>
internal sealed class BroadcastTray : IDisposable
{
    private readonly BroadcastViewModel _model;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _state = new() { IsEnabled = false };
    private readonly NativeMenuItem _goLive = new("Go Live");
    private readonly NativeMenuItem _stop = new("Stop broadcast");
    private readonly WindowIcon _idleIcon;
    private readonly WindowIcon _liveIcon;

    internal BroadcastTray(MainWindow window, BroadcastViewModel model)
    {
        _model = model;
        _idleIcon = new(AssetLoader.Open(new Uri("avares://Tropicast.Station/Assets/tropicast.ico")));
        _liveIcon = new(AssetLoader.Open(new Uri("avares://Tropicast.Station/Assets/tropicast-live.png")));
        _goLive.Command = model.GoLiveCommand;
        _stop.Command = model.StopCommand;
        Menu = new NativeMenu
        {
            Items =
            {
                _state,
                new NativeMenuItemSeparator(),
                new NativeMenuItem("Open Tropicast Station") { Command = new RelayCommand(() => window.ShowFromTray()) },
                new NativeMenuItem("Maximize window") { Command = new RelayCommand(() => window.ShowFromTray(maximize: true)) },
                new NativeMenuItemSeparator(),
                _goLive,
                _stop,
                new NativeMenuItemSeparator(),
                new NativeMenuItem("Quit Tropicast Station") { Command = new RelayCommand(window.Quit) },
            },
        };
        _tray = new TrayIcon { Menu = Menu, IsVisible = true };
        _tray.Clicked += (_, _) => window.ToggleFromTray();
        if (Application.Current is { } app)
        {
            TrayIcon.SetIcons(app, new TrayIcons { _tray });
        }
        model.PropertyChanged += OnChanged;
        model.GoLiveCommand.CanExecuteChanged += OnCommandChanged;
        model.StopCommand.CanExecuteChanged += OnCommandChanged;
        Update();
    }

    /// <summary>The right-click menu (the click menu on macOS).</summary>
    internal NativeMenu Menu { get; }

    /// <summary>The menu item labelled <paramref name="header"/>.</summary>
    internal NativeMenuItem Item(string header) =>
        Menu.Items.OfType<NativeMenuItem>().Single(item => item is not NativeMenuItemSeparator && item.Header == header);

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void OnCommandChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        _tray.ToolTipText = _model.TrayLabel;
        _state.Header = $"{_model.StateLabel} — {_model.Elapsed}";
        _goLive.IsEnabled = _model.GoLiveCommand.CanExecute(null);
        _stop.IsEnabled = _model.StopCommand.CanExecute(null);
        _tray.Icon = _model.State is BroadcastState.Live or BroadcastState.Reconnecting ? _liveIcon : _idleIcon;
    }

    public void Dispose()
    {
        _model.PropertyChanged -= OnChanged;
        _model.GoLiveCommand.CanExecuteChanged -= OnCommandChanged;
        _model.StopCommand.CanExecuteChanged -= OnCommandChanged;
        _tray.Dispose();
        GC.SuppressFinalize(this);
    }
}
