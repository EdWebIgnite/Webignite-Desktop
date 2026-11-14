using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Webignite.Core.Studio;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Studio's lines view, drawn onto a dark canvas like an underground map: a coloured line for each running
/// project, a stop for each of its headings, and a glowing marker where the work has got to. The lines draw
/// themselves in from the left when it opens, one after the other, and the markers breathe. It only asks for
/// frames while it is on screen, and with Motion set to Off it stands still. Colours come from the palette.
/// </summary>
public sealed class StudioLinesCanvas : Control
{
    public static readonly StyledProperty<IReadOnlyList<ProjectLine>?> LinesProperty =
        AvaloniaProperty.Register<StudioLinesCanvas, IReadOnlyList<ProjectLine>?>(nameof(Lines));

    private const double RowHeight = 92, Top = 26, NameWidth = 250, Right = 70;
    private const double DrawSeconds = 1.3, Stagger = 0.16;

    private double clock, opened;
    private TimeSpan? last;
    private bool asked;
    private int hover = -1;
    private readonly Dictionary<string, IBrush> brushes = [];

    static StudioLinesCanvas()
    {
        AffectsRender<StudioLinesCanvas>(LinesProperty);
        AffectsMeasure<StudioLinesCanvas>(LinesProperty);
    }

    public StudioLinesCanvas()
    {
        ClipToBounds = true;
        ResourcesChanged += (_, _) =>
        {
            brushes.Clear();
            InvalidateVisual();
        };
    }

    public IReadOnlyList<ProjectLine>? Lines { get => GetValue(LinesProperty); set => SetValue(LinesProperty, value); }

    /// <summary>A click on a line: open its project.</summary>
    public event Action<ProjectLine>? Opened;

    /// <summary>Jumps the drawing-in to its end, so a picture of the lines is the same each time.</summary>
    public void Settle()
    {
        opened = 1000;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width, Top * 2 + Math.Max(1, Lines?.Count ?? 0) * RowHeight);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Spinner.Still)
            Settle();
        Ask();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        last = null;
        brushes.Clear();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            Ask();
    }

    private void Ask()
    {
        if (asked || Spinner.Still || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } top)
            return;
        asked = true;
        top.RequestAnimationFrame(Tick);
    }

    private void Tick(TimeSpan now)
    {
        asked = false;
        if (last is { } before)
        {
            var passed = Math.Min(0.1, (now - before).TotalSeconds);
            clock += passed;
            opened += passed;
        }
        last = now;
        InvalidateVisual();
        if (IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not null)
            Ask();
        else
            last = null;
    }

    private IBrush Brush(string key)
    {
        if (brushes.TryGetValue(key, out var found))
            return found;
        var brush = this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush b ? b : Brushes.Gray;
        brushes[key] = brush;
        return brush;
    }

    private static Color ColourOf(IBrush brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;

    private FormattedText Text(string text, double size, IBrush brush, double width, bool bold = false, TextAlignment align = TextAlignment.Left)
    {
        var made = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush)
        {
            MaxTextWidth = Math.Max(20, width), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = align,
        };
        if (bold)
            made.SetFontWeight(FontWeight.SemiBold);
        return made;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush("Nav"), bounds, 14);
        var lines = Lines ?? [];
        var label = Brush("NavText");
        var soft = Brush("NavMuted");
        var ground = Brush("Nav");
        var from = NameWidth + 34;
        var to = Math.Max(from + 120, bounds.Width - Right);
        for (var row = 0; row < lines.Count; row++)
        {
            var line = lines[row];
            var y = Top + row * RowHeight + 34;
            if (y + RowHeight < 0 || y - RowHeight > bounds.Height)
                continue;
            // Each line starts drawing a moment after the one above it.
            var drawn = Math.Clamp((opened - row * Stagger) / DrawSeconds, 0, 1);
            drawn = 1 - Math.Pow(1 - drawn, 3);
            var colour = ColourOf(Brush("Project" + line.Colour));
            if (row == hover)
                context.FillRectangle(new SolidColorBrush(ColourOf(label), 0.05), new Rect(10, y - 34, bounds.Width - 20, RowHeight - 8), 10);

            context.DrawText(Text(line.Name, 14, label, NameWidth, bold: true), new Point(26, y - 20));
            context.DrawText(Text(line.Client.Length > 0 ? $"{line.Client}  ·  {line.Detail}" : line.Detail, 11.5, soft, NameWidth), new Point(26, y + 1));

            var stops = line.Stops.Count;
            var gap = stops > 1 ? (to - from) / (stops - 1) : 0;
            var reached = line.Position * drawn;
            var end = from + gap * (stops - 1) * drawn;
            var markerX = from + gap * reached;
            // The whole route faintly, then the part already travelled in full colour.
            context.DrawLine(new Pen(new SolidColorBrush(colour, 0.4), 6, lineCap: PenLineCap.Round), new Point(from, y), new Point(end, y));
            if (markerX > from)
                context.DrawLine(new Pen(new SolidColorBrush(colour), 6, lineCap: PenLineCap.Round), new Point(from, y), new Point(markerX, y));

            for (var i = 0; i < stops; i++)
            {
                var x = from + gap * i;
                if (x > end + 0.5)
                    break;
                var passed = i <= reached + 0.001;
                var finished = line.Stops[i].Finished || (i == stops - 1 && line.Position >= stops - 1);
                context.DrawEllipse(finished && passed ? new SolidColorBrush(colour) : ground, new Pen(new SolidColorBrush(colour, passed ? 1 : 0.45), 3), new Point(x, y), 8, 8);
                var name = Text(line.Stops[i].Name, 11.5, passed ? label : soft, Math.Max(60, gap - 10), align: TextAlignment.Center);
                var width = Math.Max(60, gap - 10);
                // The first and last names stay inside the canvas.
                var left = Math.Clamp(x - width / 2, from - 30, Math.Max(from - 30, bounds.Width - width - 8));
                context.DrawText(name, new Point(left, y + 15));
            }

            // Where the work has got to: a glowing marker that breathes.
            var beat = 0.5 + 0.5 * Math.Sin(clock * 2 + row);
            var glow = 20 + 5 * beat;
            context.DrawEllipse(new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(170 * (0.6 + 0.4 * beat)), colour.R, colour.G, colour.B), 0),
                    new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 1),
                },
            }, null, new Point(markerX, y), glow, glow);
            context.DrawEllipse(label, new Pen(new SolidColorBrush(colour), 3), new Point(markerX, y), 6.5, 6.5);
        }
    }

    private int RowAt(Point point)
    {
        var row = (int)Math.Floor((point.Y - Top) / RowHeight);
        return row >= 0 && row < (Lines?.Count ?? 0) ? row : -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var row = RowAt(e.GetPosition(this));
        if (row == hover)
            return;
        hover = row;
        Cursor = row < 0 ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hover = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (RowAt(e.GetPosition(this)) is var row and >= 0 && Lines is { } lines)
            Opened?.Invoke(lines[row]);
    }
}
