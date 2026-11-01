using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Webignite.Core.Tools;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Shows a screenshot fitted to the space and lets marks be drawn on it. Marks are kept in the image's own
/// pixels, so the saved picture matches what was drawn whatever the window size.
/// </summary>
public sealed class MarkupCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> ImageProperty = AvaloniaProperty.Register<MarkupCanvas, Bitmap?>(nameof(Image));
    public static readonly StyledProperty<IReadOnlyList<Mark>> MarksProperty = AvaloniaProperty.Register<MarkupCanvas, IReadOnlyList<Mark>>(nameof(Marks), []);
    public static readonly StyledProperty<MarkKind> ToolProperty = AvaloniaProperty.Register<MarkupCanvas, MarkKind>(nameof(Tool), MarkKind.Pin);
    public static readonly StyledProperty<MarkColour> PenProperty = AvaloniaProperty.Register<MarkupCanvas, MarkColour>(nameof(Pen), MarkColour.Red);

    static MarkupCanvas() => AffectsRender<MarkupCanvas>(ImageProperty, MarksProperty);

    public Bitmap? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public IReadOnlyList<Mark> Marks { get => GetValue(MarksProperty); set => SetValue(MarksProperty, value); }
    public MarkKind Tool { get => GetValue(ToolProperty); set => SetValue(ToolProperty, value); }
    public MarkColour Pen { get => GetValue(PenProperty); set => SetValue(PenProperty, value); }

    /// <summary>Called with each finished mark; the owner decides whether to keep it.</summary>
    public Action<Mark>? MarkDrawn { get; set; }

    private List<MarkPoint>? drawing;

    public MarkupCanvas()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    private (double Scale, Point Offset) Fit()
    {
        if (Image is not { } image || image.PixelSize.Width == 0)
            return (1, default);
        var w = image.PixelSize.Width;
        var h = image.PixelSize.Height;
        var scale = Math.Min(1, Math.Min(Bounds.Width / w, Bounds.Height / h));
        return (scale, new Point((Bounds.Width - w * scale) / 2, (Bounds.Height - h * scale) / 2));
    }

    private MarkPoint ToImage(Point p)
    {
        var (scale, offset) = Fit();
        var w = Image?.PixelSize.Width ?? 0;
        var h = Image?.PixelSize.Height ?? 0;
        return new MarkPoint(Math.Clamp((p.X - offset.X) / scale, 0, w), Math.Clamp((p.Y - offset.Y) / scale, 0, h));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Image is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        drawing = [ToImage(e.GetPosition(this))];
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (drawing is null)
            return;
        var point = ToImage(e.GetPosition(this));
        if (Tool == MarkKind.Pen)
            drawing.Add(point);
        else if (drawing.Count == 1)
            drawing.Add(point);
        else
            drawing[^1] = point;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (drawing is null)
            return;
        var points = drawing;
        drawing = null;
        e.Pointer.Capture(null);
        if (Tool == MarkKind.Pin || points.Count > 1)
            MarkDrawn?.Invoke(new Mark(Tool, Pen, points));
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (Image is not { } image)
            return;
        var (scale, offset) = Fit();
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            context.DrawImage(image, new Rect(0, 0, image.PixelSize.Width, image.PixelSize.Height));
            var marks = drawing is { Count: > 0 } ? [.. Marks, new Mark(Tool, Pen, drawing) { Number = Markup.NextNumber(Marks) }] : Marks;
            Draw(context, marks, image.PixelSize.Width, image.PixelSize.Height);
        }
    }

    /// <summary>Draws marks in image pixels. Used on screen and for the saved picture.</summary>
    public static void Draw(DrawingContext context, IEnumerable<Mark> marks, int width, int height)
    {
        var stroke = Markup.StrokeFor(width, height);
        foreach (var mark in marks)
        {
            var colour = ColourOf(mark.Colour);
            var pen = new Pen(new SolidColorBrush(colour), stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var p = mark.Points;
            switch (mark.Kind)
            {
                case MarkKind.Pin:
                    var radius = stroke * 5;
                    var centre = new Point(p[0].X, p[0].Y);
                    context.DrawEllipse(new SolidColorBrush(colour), new Pen(Brushes.White, stroke / 2), centre, radius, radius);
                    var text = new FormattedText(mark.Number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), radius * 1.15,
                        mark.Colour == MarkColour.Yellow ? Brushes.Black : Brushes.White);
                    context.DrawText(text, new Point(centre.X - text.Width / 2, centre.Y - text.Height / 2));
                    break;
                case MarkKind.Arrow when p.Count > 1:
                    var from = new Point(p[0].X, p[0].Y);
                    var to = new Point(p[^1].X, p[^1].Y);
                    context.DrawLine(pen, from, to);
                    var (left, right) = Markup.ArrowHead(p[0], p[^1], stroke * 6);
                    context.DrawLine(pen, to, new Point(left.X, left.Y));
                    context.DrawLine(pen, to, new Point(right.X, right.Y));
                    break;
                case MarkKind.Box when p.Count > 1:
                    var (x, y, w, h) = Markup.Box(p[0], p[^1]);
                    context.DrawRectangle(null, pen, new Rect(x, y, w, h), stroke, stroke);
                    break;
                case MarkKind.Highlight when p.Count > 1:
                    var (hx, hy, hw, hh) = Markup.Box(p[0], p[^1]);
                    context.DrawRectangle(new SolidColorBrush(colour, 0.35), null, new Rect(hx, hy, hw, hh));
                    break;
                case MarkKind.Pen when p.Count > 1:
                    var geometry = new StreamGeometry();
                    using (var g = geometry.Open())
                    {
                        g.BeginFigure(new Point(p[0].X, p[0].Y), false);
                        foreach (var point in p.Skip(1))
                            g.LineTo(new Point(point.X, point.Y));
                        g.EndFigure(false);
                    }
                    context.DrawGeometry(null, pen, geometry);
                    break;
            }
        }
    }

    public static Color ColourOf(MarkColour colour)
    {
        var key = colour switch { MarkColour.Yellow => "MarkYellow", MarkColour.Blue => "MarkBlue", _ => "MarkRed" };
        return Application.Current?.TryGetResource(key, null, out var value) == true && value is Color c ? c : Colors.Black;
    }

    /// <summary>The screenshot with its marks, at full size, as PNG.</summary>
    public static byte[] RenderPng(Bitmap image, IEnumerable<Mark> marks)
    {
        var size = image.PixelSize;
        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            context.DrawImage(image, new Rect(0, 0, size.Width, size.Height));
            Draw(context, marks, size.Width, size.Height);
        }
        using var stream = new MemoryStream();
        target.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }
}
