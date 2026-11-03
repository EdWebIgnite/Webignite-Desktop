using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A bar behind an amount, filling <see cref="Fraction"/> of the row from the left, so the biggest amount in a
/// list has the longest bar. Put it under the row's text in a Panel. Keep the fill faint so the text reads over it.
/// </summary>
public sealed class ShareBar : Control
{
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<ShareBar, double>(nameof(Fraction));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<ShareBar, IBrush?>(nameof(Fill));

    static ShareBar() => AffectsRender<ShareBar>(FractionProperty, FillProperty);

    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width * Math.Clamp(Fraction, 0, 1);
        if (width < 1 || Fill is not { } fill)
            return;
        context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, 0, width, Bounds.Height), 4));
    }
}
