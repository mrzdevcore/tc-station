using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace Tropicast.Station.App.Tests;

/// <summary>
/// Finds a named control anywhere under a root control. Pages are user controls with their own
/// name scopes, so <c>FindControl</c> on the window no longer reaches their controls.
/// </summary>
internal static class ControlLookup
{
    public static T? Find<T>(this Control root, string name) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);
}
