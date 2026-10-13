using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Tropicast.Station.App.Controls;

public static class Converters
{
    // The parameters are a few constants from XAML: split each once.
    private static readonly ConcurrentDictionary<string, string[]> Names = new(StringComparer.Ordinal);

    /// <summary>
    /// True when the value's name is one of the parameter's names ("Live", or "Connecting|Reconnecting"),
    /// e.g. a page or a broadcast state.
    /// </summary>
    public static readonly IValueConverter Is = new FuncValueConverter<object?, object?, bool>(
        (value, parameter) => value is not null && parameter is string names
            && Names.GetOrAdd(names, static text => text.Split('|')).Contains(value.ToString(), StringComparer.Ordinal));

    /// <summary>Looks up a line icon geometry by name ("Broadcast" finds the "Icon.Broadcast" resource).</summary>
    public static readonly IValueConverter Icon = new FuncValueConverter<string?, Geometry?>(name =>
        name is not null && Application.Current?.TryFindResource($"Icon.{name}", out var icon) == true ? icon as Geometry : null);
}
