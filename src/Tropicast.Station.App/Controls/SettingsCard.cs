using Avalonia;
using Avalonia.Controls;
using Avalonia.Metadata;

namespace Tropicast.Station.App.Controls;

/// <summary>A rounded card stacking <see cref="SettingRow"/>s, with a divider between visible rows.</summary>
public sealed class SettingsCard : Border
{
    private readonly RowStack _rows = new();

    public SettingsCard() => Child = _rows;

    [Content]
    public Avalonia.Controls.Controls Children => _rows.Children;

    /// <summary>Marks the first shown row, which draws no divider above it.</summary>
    private sealed class RowStack : StackPanel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var first = true;
            foreach (var child in Children)
            {
                var shown = child.IsVisible && child is not SettingRow { IsFilteredOut: true };
                if (child is SettingRow row)
                {
                    row.SetFirst(first && shown);
                }
                first &= !shown;
            }
            return base.MeasureOverride(availableSize);
        }
    }
}
