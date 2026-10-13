using Avalonia.Controls;

namespace Tropicast.Station.App;

/// <summary>Tells the user, once, that the closed or minimized window keeps running in the tray.</summary>
public interface ITrayNotice
{
    Task ShowAsync(Window owner);
}

internal sealed class TrayNotice : ITrayNotice
{
    public Task ShowAsync(Window owner) => MessageDialog.ShowAsync(owner, "Still running in the tray",
        "Tropicast Station keeps running in the system tray, and a live broadcast goes on. "
            + "Right-click its icon to open the window or quit. You can turn this off in Settings, General.",
        ["OK"], primary: 0);
}
