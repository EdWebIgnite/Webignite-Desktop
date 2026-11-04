using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Webignite.Core.Tools;

namespace Webignite.Desktop.Views;

/// <summary>
/// The frozen screen, shown over the real one: drag to pick an area, click to take the window under the mouse,
/// Enter for the whole screen, Esc to stop. Covers one monitor, so screens with different scaling stay sharp.
/// </summary>
public sealed class ScreenGrabWindow : Window
{
    private readonly GrabCanvas canvas;
    private readonly TaskCompletionSource<ScreenGrab.Area?> chosen = new();

    public ScreenGrabWindow(Bitmap frozen, PixelRect bounds, double scaling, IReadOnlyList<ScreenGrab.Area> windows)
    {
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = bounds.Position;
        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;
        Cursor = new Cursor(StandardCursorType.Cross);
        canvas = new GrabCanvas(frozen, scaling, windows) { Done = area => Finish(area) };
        Content = canvas;
        Closed += (_, _) => chosen.TrySetResult(null);
    }

    /// <summary>The area picked, in pixels of the frozen picture, or null when stopped.</summary>
    public Task<ScreenGrab.Area?> Picked => chosen.Task;

    public GrabCanvas Canvas => canvas;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Activate();
        canvas.Focus();
    }

    private void Finish(ScreenGrab.Area? area)
    {
        chosen.TrySetResult(area);
        Close();
    }

    /// <summary>A copy of part of a picture, as a picture of its own.</summary>
    public static Bitmap Crop(Bitmap source, ScreenGrab.Area area)
    {
        var size = new PixelSize(area.Width, area.Height);
        var copy = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = copy.Lock())
            source.CopyPixels(new PixelRect(area.X, area.Y, area.Width, area.Height), buffer.Address, buffer.RowBytes * area.Height, buffer.RowBytes);
        return copy;
    }
}

/// <summary>Draws the frozen screen dimmed, with the part being picked clear and its size shown.</summary>
public sealed class GrabCanvas : Control
{
    private readonly Bitmap frozen;
    private readonly double scaling;
    private readonly IReadOnlyList<ScreenGrab.Area> windows;
    private (int X, int Y)? pressedAt;
    private ScreenGrab.Area? showing;

    public GrabCanvas(Bitmap frozen, double scaling, IReadOnlyList<ScreenGrab.Area> windows)
    {
        this.frozen = frozen;
        this.scaling = scaling;
        this.windows = windows;
        Focusable = true;
    }

    internal Action<ScreenGrab.Area?>? Done { get; init; }

    private ScreenGrab.Area Whole => new(0, 0, frozen.PixelSize.Width, frozen.PixelSize.Height);

    private (int X, int Y) Pixel(Point p) => ((int)Math.Round(p.X * scaling), (int)Math.Round(p.Y * scaling));

    // The pointer as pixels of the frozen picture. Public so the screenshot tool can drive it without a mouse.
    public void Press(Point at)
    {
        pressedAt = Pixel(at);
        Hover(at);
    }

    public void Hover(Point at)
    {
        var (x, y) = Pixel(at);
        showing = pressedAt is { } start && !ScreenGrab.IsClick(start.X, start.Y, x, y)
            ? ScreenGrab.Region(start.X, start.Y, x, y, Whole)
            : ScreenGrab.WindowAt(x, y, windows, Whole);
        InvalidateVisual();
    }

    public void Release(Point at)
    {
        if (pressedAt is not { } start)
            return;
        var (x, y) = Pixel(at);
        pressedAt = null;
        var area = ScreenGrab.IsClick(start.X, start.Y, x, y) ? ScreenGrab.WindowAt(x, y, windows, Whole) : ScreenGrab.Region(start.X, start.Y, x, y, Whole);
        Done?.Invoke(area.IsEmpty ? null : area);
    }

    public void WholeScreen() => Done?.Invoke(Whole);

    public void Stop() => Done?.Invoke(null);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            Stop();
            return;
        }
        e.Pointer.Capture(this);
        Press(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Hover(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        Release(e.GetPosition(this));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            Stop();
        else if (e.Key == Key.Enter)
            WholeScreen();
    }

    public override void Render(DrawingContext context)
    {
        var all = new Rect(Bounds.Size);
        context.DrawImage(frozen, all);
        var dim = Brush("Scrim", Color.FromArgb(0x66, 0, 0, 0));
        if (showing is not { } area)
        {
            context.FillRectangle(dim, all);
        }
        else
        {
            var clear = new Rect(area.X / scaling, area.Y / scaling, area.Width / scaling, area.Height / scaling);
            // Dim all round the clear part: above, below, left and right of it.
            context.FillRectangle(dim, new Rect(0, 0, all.Width, clear.Top));
            context.FillRectangle(dim, new Rect(0, clear.Bottom, all.Width, Math.Max(0, all.Height - clear.Bottom)));
            context.FillRectangle(dim, new Rect(0, clear.Top, clear.Left, clear.Height));
            context.FillRectangle(dim, new Rect(clear.Right, clear.Top, Math.Max(0, all.Width - clear.Right), clear.Height));
            context.DrawRectangle(null, new Pen(Brush("Surface", Colors.White), 2), clear);
            var size = Text($"{area.Width} × {area.Height}", 12);
            var label = new Point(clear.Left, clear.Top > size.Height + 10 ? clear.Top - size.Height - 8 : clear.Bottom + 6);
            context.FillRectangle(Brush("Nav", Colors.Black), new Rect(label.X, label.Y, size.Width + 12, size.Height + 4), 4);
            context.DrawText(size, new Point(label.X + 6, label.Y + 2));
        }
        var help = Text("Drag to pick an area, click a window, or press Enter for the whole screen. Esc to stop.", 14);
        var at = new Point((all.Width - help.Width) / 2, 24);
        context.FillRectangle(Brush("Nav", Colors.Black), new Rect(at.X - 14, at.Y - 8, help.Width + 28, help.Height + 16), 8);
        context.DrawText(help, at);
    }

    private static FormattedText Text(string words, double size) =>
        new(words, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, Brush("NavText", Colors.White));

    private static IBrush Brush(string key, Color fallback) =>
        Application.Current?.TryGetResource(key, null, out var found) == true && found is IBrush brush ? brush : new SolidColorBrush(fallback);
}
