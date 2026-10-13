using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Tropicast.Station.App;

/// <summary>Tells the user, once, that the closed or minimized window keeps running in the tray.</summary>
public interface ITrayNotice
{
    Task ShowAsync(Window owner);
}

internal sealed class TrayNotice : ITrayNotice
{
    public async Task ShowAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var dialog = new Window
        {
            Title = "Still running in the tray",
            Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "Tropicast Station keeps running in the system tray, and a live broadcast goes on. "
                        + "Right-click its icon to open the window or quit. You can turn this off in Settings, General.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                ok,
            },
        };
        dialog.Opened += (_, _) => ok.Focus();
        await dialog.ShowDialog(owner);
    }
}
