using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A small trend line with no axes, for a figure on a card: one point per month, oldest on the left, with a dot
/// on the latest. The line uses <see cref="Stroke"/> and a faint fill under it.
/// </summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    static Sparkline() => AffectsRender<Sparkline>(ValuesProperty, StrokeProperty);

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        if (values is not { Count: >= 2 } || Stroke is not { } stroke)
            return;

        const double dot = 2.5;
        var width = Bounds.Width - dot * 2;
        var height = Bounds.Height - dot * 2;
        var max = values.Max();
        var min = Math.Min(0, values.Min());
        var span = max - min;

        Point At(int i) => new(
            dot + width * i / (values.Count - 1),
            dot + (span <= 0 ? height : height - (values[i] - min) / span * height));

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(At(0), false);
            for (var i = 1; i < values.Count; i++)
                g.LineTo(At(i));
            g.EndFigure(false);
        }

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(At(0).X, dot + height), true);
            for (var i = 0; i < values.Count; i++)
                g.LineTo(At(i));
            g.LineTo(new Point(At(values.Count - 1).X, dot + height));
            g.EndFigure(true);
        }

        var fill = stroke is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.12) : null;
        if (fill is not null)
            context.DrawGeometry(fill, null, area);
        context.DrawGeometry(null, new Pen(stroke, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
        context.DrawEllipse(stroke, null, At(values.Count - 1), dot, dot);
    }
}
