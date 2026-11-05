using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Webignite.Core.Tools;

namespace Webignite.Desktop.Views;

/// <summary>
/// The eyedropper: the frozen screen shown over the real one, with a magnified square round the pointer and the
/// colour's code. A click keeps the colour under the pointer, Enter keeps it too, Esc or a right-click stops.
/// Covers one monitor, like the screenshot picker.
/// </summary>
public sealed class ColourPickWindow : Window
{
    private readonly TaskCompletionSource<Rgb?> chosen = new();

    public ColourPickWindow(Bitmap frozen, PixelRect bounds, double scaling)
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
        Canvas = new ColourPickCanvas(frozen, scaling) { Done = Finish };
        Content = Canvas;
        Closed += (_, _) => chosen.TrySetResult(null);
    }

    /// <summary>The colour picked, or null when stopped.</summary>
    public Task<Rgb?> Picked => chosen.Task;

    public ColourPickCanvas Canvas { get; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Activate();
        Canvas.Focus();
    }

    private void Finish(Rgb? colour)
    {
        chosen.TrySetResult(colour);
        Close();
    }
}

/// <summary>Draws the frozen screen with the loupe; reads only the few pixels round the pointer each move.</summary>
public sealed class ColourPickCanvas : Control
{
    private readonly Bitmap frozen;
    private readonly double scaling;
    private (int X, int Y) pointer;
    private ScreenGrab.Area square;
    private uint[] pixels = [];
    private readonly bool rgba;

    public ColourPickCanvas(Bitmap frozen, double scaling)
    {
        this.frozen = frozen;
        this.scaling = scaling;
        // Windows' screen grab is BGRA; a picture from elsewhere (the screenshot tool) may be RGBA.
        rgba = frozen.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        Focusable = true;
    }

    internal Action<Rgb?>? Done { get; init; }

    /// <summary>The colour under the pointer now.</summary>
    public Rgb? Under { get; private set; }

    // Public so the screenshot tool can drive it without a mouse.
    public void Hover(Point at)
    {
        var size = frozen.PixelSize;
        pointer = (Math.Clamp((int)(at.X * scaling), 0, size.Width - 1), Math.Clamp((int)(at.Y * scaling), 0, size.Height - 1));
        square = ColourLoupe.Around(pointer.X, pointer.Y, size.Width, size.Height);
        pixels = Read(square);
        var index = (pointer.Y - square.Y) * square.Width + (pointer.X - square.X);
        Under = index >= 0 && index < pixels.Length ? ToRgb(pixels[index]) : null;
        InvalidateVisual();
    }

    public void Pick(Point at)
    {
        Hover(at);
        Done?.Invoke(Under);
    }

    public void Stop() => Done?.Invoke(null);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Hover(e.GetPosition(this));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            Stop();
        else
            Pick(e.GetPosition(this));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            Stop();
        else if (e.Key == Key.Enter && Under is not null)
            Done?.Invoke(Under);
    }

    private uint[] Read(ScreenGrab.Area area)
    {
        var found = new uint[area.Width * area.Height];
        var handle = GCHandle.Alloc(found, GCHandleType.Pinned);
        try
        {
            frozen.CopyPixels(new PixelRect(area.X, area.Y, area.Width, area.Height), handle.AddrOfPinnedObject(), found.Length * 4, area.Width * 4);
        }
        finally
        {
            handle.Free();
        }
        return found;
    }

    // BGRA keeps blue in the lowest byte, RGBA red.
    private Rgb ToRgb(uint pixel) => rgba
        ? new((byte)(pixel & 0xFF), (byte)(pixel >> 8 & 0xFF), (byte)(pixel >> 16 & 0xFF))
        : new((byte)(pixel >> 16 & 0xFF), (byte)(pixel >> 8 & 0xFF), (byte)(pixel & 0xFF));

    public override void Render(DrawingContext context)
    {
        var all = new Rect(Bounds.Size);
        context.DrawImage(frozen, all);
        var help = Text("Click a colour to copy its code. Esc to stop.", 14, "NavText");
        var helpAt = new Point((all.Width - help.Width) / 2, 24);
        context.FillRectangle(Brush("Nav", Colors.Black), new Rect(helpAt.X - 14, helpAt.Y - 8, help.Width + 28, help.Height + 16), 8);
        context.DrawText(help, helpAt);
        if (pixels.Length == 0 || Under is not { } under)
            return;

        // The loupe sits below and right of the pointer, and flips when that would run off the screen.
        var cell = ColourLoupe.Zoom;
        var loupe = new Size(square.Width * cell, square.Height * cell + 30);
        var at = new Point(pointer.X / scaling + 24, pointer.Y / scaling + 24);
        if (at.X + loupe.Width > all.Width)
            at = at.WithX(pointer.X / scaling - 24 - loupe.Width);
        if (at.Y + loupe.Height > all.Height)
            at = at.WithY(pointer.Y / scaling - 24 - loupe.Height);
        var frame = new Rect(at, loupe);
        context.FillRectangle(Brush("Nav", Colors.Black), frame.Inflate(3), 6);
        for (var row = 0; row < square.Height; row++)
        {
            for (var col = 0; col < square.Width; col++)
            {
                var c = ToRgb(pixels[row * square.Width + col]);
                context.FillRectangle(new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), new Rect(at.X + col * cell, at.Y + row * cell, cell, cell));
            }
        }
        var centre = new Rect(at.X + (pointer.X - square.X) * cell, at.Y + (pointer.Y - square.Y) * cell, cell, cell);
        context.DrawRectangle(null, new Pen(Brush("Surface", Colors.White), 2), centre);
        var code = Text(under.Hex, 13, "NavText");
        var swatch = new Rect(at.X + 6, at.Y + square.Height * cell + 8, 14, 14);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(under.R, under.G, under.B)), swatch, 3);
        context.DrawText(code, new Point(swatch.Right + 8, swatch.Top - 1));
    }

    private static FormattedText Text(string words, double size, string colour) =>
        new(words, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, Brush(colour, Colors.White));

    private static IBrush Brush(string key, Color fallback) =>
        Application.Current?.TryGetResource(key, null, out var found) == true && found is IBrush brush ? brush : new SolidColorBrush(fallback);
}
