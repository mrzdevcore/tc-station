using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Tropicast.Station.App.Controls;

/// <summary>
/// Draws a 24×24 line-icon geometry (see Styles/Icons.axaml) with a 2px round stroke in the inherited text colour.
/// Decorative: screen readers skip it, the surrounding control carries the name.
/// </summary>
public sealed class LineIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<LineIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<LineIcon>();

    static LineIcon()
    {
        AffectsRender<LineIcon>(DataProperty, ForegroundProperty);
        WidthProperty.OverrideDefaultValue<LineIcon>(16);
        HeightProperty.OverrideDefaultValue<LineIcon>(16);
        Avalonia.Automation.AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<LineIcon>(
            Avalonia.Automation.AccessibilityView.Raw);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is null || Foreground is null)
        {
            return;
        }
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(null, new Pen(Foreground, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Data);
        }
    }
}
