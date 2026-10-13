using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Tropicast.Station.App;

/// <summary>The app's small modal: a wrapped message over a row of buttons. Escape or closing picks <c>cancel</c>.</summary>
internal static class MessageDialog
{
    /// <returns>The index of the button pressed.</returns>
    internal static Task<int> ShowAsync(Window owner, string title, string message, IReadOnlyList<string> buttons,
        int cancel = 0, int focus = 0, int primary = -1)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(buttons);
        var dialog = new Window
        {
            Title = title, Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], IsCancel = i == cancel, IsDefault = i == primary };
            if (i == primary)
            {
                button.Classes.Add("accent");
            }
            button.Click += (_, _) => dialog.Close(index);
            row.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                row,
            },
        };
        dialog.Opened += (_, _) => row.Children[focus].Focus();
        dialog.Closing += (_, e) =>
        {
            // Closed from the title bar: same answer as Escape.
            if (!e.IsProgrammatic)
            {
                e.Cancel = true;
                dialog.Close(cancel);
            }
        };
        return dialog.ShowDialog<int>(owner);
    }
}
