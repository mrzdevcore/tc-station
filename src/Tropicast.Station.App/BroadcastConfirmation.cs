using Avalonia.Controls;
using Tropicast.Station.App.Views;

namespace Tropicast.Station.App;

public interface IBroadcastConfirmation
{
    Task<bool> ConfirmStopAsync(bool closing);
}

internal sealed class BroadcastConfirmation : IBroadcastConfirmation
{
    internal Window? Owner { get; set; }

    public async Task<bool> ConfirmStopAsync(bool closing)
    {
        var owner = Owner ?? throw new InvalidOperationException("Broadcast confirmation needs the main window.");
        // The window may be hidden in the tray (tray Stop, Cmd+Q, OS shutdown) or minimized: bring it back as it was,
        // maximized included, so the question shows on top of it.
        if (owner is MainWindow main)
        {
            main.ShowFromTray();
        }
        else
        {
            owner.Show();
            if (owner.WindowState == WindowState.Minimized)
            {
                owner.WindowState = WindowState.Normal;
            }
            owner.Activate();
        }
        var choice = await MessageDialog.ShowAsync(owner,
            closing ? "Stop broadcast and quit?" : "Stop broadcast?",
            "Your station is broadcasting. Stopping disconnects its live stream.",
            ["Keep broadcasting", closing ? "Stop and quit" : "Stop broadcast"]);
        return choice == 1;
    }
}
