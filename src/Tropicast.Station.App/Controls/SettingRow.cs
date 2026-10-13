using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Tropicast.Station.App.Controls;

/// <summary>
/// One settings row: a title and an optional description on the left, the control (its content) on the right.
/// Below <see cref="NarrowWidth"/> the control wraps under the text.
/// </summary>
public sealed class SettingRow : ContentControl
{
    private const double NarrowWidth = 480;

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    /// <summary>Hidden by the settings search; kept apart from IsVisible, which pages bind for their own logic.</summary>
    public static readonly StyledProperty<bool> IsFilteredOutProperty =
        AvaloniaProperty.Register<SettingRow, bool>(nameof(IsFilteredOut));

    static SettingRow()
    {
        IsFilteredOutProperty.Changed.AddClassHandler<SettingRow>((row, _) =>
        {
            row.PseudoClasses.Set(":filtered", row.IsFilteredOut);
            (row.Parent as Layoutable)?.InvalidateMeasure();
        });
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsFilteredOut
    {
        get => GetValue(IsFilteredOutProperty);
        set => SetValue(IsFilteredOutProperty, value);
    }

    /// <summary>Set by <see cref="SettingsCard"/> on its first visible row, which draws no divider.</summary>
    internal void SetFirst(bool first) => PseudoClasses.Set(":first", first);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        PseudoClasses.Set(":narrow", e.NewSize.Width < NarrowWidth);
    }

    internal bool Matches(string query) =>
        (Title?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);
}
