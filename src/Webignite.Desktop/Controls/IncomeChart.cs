using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Webignite.Core.Documents;
using Webignite.Core.Finance;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Expected against came in, one column per month, drawn straight onto the canvas so it costs
/// next to nothing in memory. Colours come from the palette: Primary for money in, a pale Primary
/// for what was expected, Muted for the scale.
/// </summary>
public sealed class IncomeChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ProjectionMonth>?> MonthsProperty =
        AvaloniaProperty.Register<IncomeChart, IReadOnlyList<ProjectionMonth>?>(nameof(Months));

    public static readonly StyledProperty<ChartStyle> ChartStyleProperty =
        AvaloniaProperty.Register<IncomeChart, ChartStyle>(nameof(ChartStyle));

    private const double Left = 64, Bottom = 26, Top = 10;
    private int hover = -1;

    static IncomeChart()
    {
        AffectsRender<IncomeChart>(MonthsProperty, ChartStyleProperty);
    }

    public IReadOnlyList<ProjectionMonth>? Months { get => GetValue(MonthsProperty); set => SetValue(MonthsProperty, value); }
    public ChartStyle ChartStyle { get => GetValue(ChartStyleProperty); set => SetValue(ChartStyleProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, 220);

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private static IBrush Faded(IBrush brush, double opacity) =>
        brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : brush;

    private Rect Plot => new(Left, Top, Math.Max(0, Bounds.Width - Left - 4), Math.Max(0, Bounds.Height - Top - Bottom));

    public override void Render(DrawingContext context)
    {
        if (Months is not { Count: > 0 } months)
            return;
        var plot = Plot;
        var primary = Brush("Primary");
        var muted = Brush("Muted");
        var line = Brush("Line");
        var text = Brush("Text");

        var top = NiceTop(months.Max(m => Math.Max(m.Expected ?? 0, m.CameIn)));
        double Y(decimal amount) => plot.Bottom - (double)(amount / top) * plot.Height;

        // Scale: four steps with the amounts on the left.
        for (var i = 0; i <= 4; i++)
        {
            var amount = top * i / 4;
            var y = Y(amount);
            context.DrawLine(new Pen(line, 1), new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(Short(amount), 11, muted);
            context.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
        }

        var slot = plot.Width / months.Count;
        if (hover >= 0 && hover < months.Count)
            context.FillRectangle(Faded(line, 0.6), new Rect(plot.Left + slot * hover, plot.Top, slot, plot.Height), 4);

        var expectedBrush = Faded(primary, 0.22);
        var points = new List<Point>();
        var expectedPoints = new List<Point?>();
        for (var i = 0; i < months.Count; i++)
        {
            var m = months[i];
            var x = plot.Left + slot * i;
            var bar = Math.Min(28, slot * 0.6);
            var expected = m.Expected ?? 0;
            switch (ChartStyle)
            {
                case ChartStyle.Bars:
                    var half = bar / 2;
                    var mid = x + slot / 2;
                    if (expected > 0)
                        context.FillRectangle(expectedBrush, new Rect(mid - half - 1, Y(expected), half, plot.Bottom - Y(expected)), 3);
                    if (m.CameIn > 0)
                        context.FillRectangle(primary, new Rect(mid + 1, Y(m.CameIn), half, plot.Bottom - Y(m.CameIn)), 3);
                    break;
                case ChartStyle.Filled:
                    var rect = new Rect(x + (slot - bar) / 2, 0, bar, 0);
                    if (expected > 0)
                        context.DrawRectangle(expectedBrush, new Pen(Faded(primary, 0.5), 1),
                            rect.WithY(Y(expected)).WithHeight(plot.Bottom - Y(expected)), 4, 4);
                    if (m.CameIn > 0)
                        context.FillRectangle(primary, rect.WithY(Y(m.CameIn)).WithHeight(plot.Bottom - Y(m.CameIn)), 4);
                    break;
                case ChartStyle.Lines:
                    var centre = x + slot / 2;
                    expectedPoints.Add(m.Expected is null ? null : new Point(centre, Y(expected)));
                    if (!m.IsFuture)
                        points.Add(new Point(centre, Y(m.CameIn)));
                    break;
            }

            var month = Text(m.Month.ToString("MMM", CultureInfo.InvariantCulture), 11, m.IsCurrent ? text : muted, m.IsCurrent);
            context.DrawText(month, new Point(x + (slot - month.Width) / 2, plot.Bottom + 8));
        }

        if (ChartStyle == ChartStyle.Lines)
        {
            var dashed = new Pen(Faded(primary, 0.6), 2, new DashStyle([3, 3], 0), PenLineCap.Round);
            for (var i = 1; i < expectedPoints.Count; i++)
                if (expectedPoints[i - 1] is { } a && expectedPoints[i] is { } b)
                    context.DrawLine(dashed, a, b);
            var solid = new Pen(primary, 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            for (var i = 1; i < points.Count; i++)
                context.DrawLine(solid, points[i - 1], points[i]);
            foreach (var p in points)
                context.DrawEllipse(primary, null, p, 3.5, 3.5);
        }

        // Where today is: a thin line between this month and next.
        var now = months.ToList().FindIndex(m => m.IsCurrent);
        if (now >= 0 && now < months.Count - 1)
        {
            var x = plot.Left + slot * (now + 1);
            context.DrawLine(new Pen(muted, 1, new DashStyle([2, 3], 0)), new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Months is not { Count: > 0 } months)
            return;
        var plot = Plot;
        var x = e.GetPosition(this).X - plot.Left;
        var index = x < 0 || x > plot.Width ? -1 : (int)(x / (plot.Width / months.Count));
        if (index == hover)
            return;
        hover = index;
        ToolTip.SetTip(this, index < 0 ? null : Describe(months[index]));
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetIsOpen(this, index >= 0);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hover = -1;
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    private static string Describe(ProjectionMonth m)
    {
        var title = m.Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var expected = m.Expected is { } e ? Money.Rand(e) : "Not set";
        return m.IsFuture
            ? $"{title}\nExpected {expected}"
            : $"{title}\nExpected {expected}\nCame in {Money.Rand(m.CameIn)}";
    }

    private FormattedText Text(string value, double size, IBrush brush, bool bold = false) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Avalonia.Controls.Documents.TextElement.GetFontFamily(this), weight: bold ? FontWeight.SemiBold : FontWeight.Normal), size, brush);

    /// <summary>A round top for the scale: 1, 2, 2.5 or 5 times a power of ten, at least R 1 000.</summary>
    internal static decimal NiceTop(decimal max)
    {
        if (max <= 1000)
            return 1000;
        var power = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)max)));
        foreach (var step in new[] { 1m, 2m, 2.5m, 5m, 10m })
            if (step * power >= max)
                return step * power;
        return 10 * power;
    }

    /// <summary>R 12k, R 1.5k, R 800.</summary>
    internal static string Short(decimal amount) => amount switch
    {
        0 => "R 0",
        >= 1_000_000 => $"R {(amount / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture)}m",
        >= 1000 => $"R {(amount / 1000).ToString("0.#", CultureInfo.InvariantCulture)}k",
        _ => $"R {amount.ToString("0", CultureInfo.InvariantCulture)}",
    };
}
