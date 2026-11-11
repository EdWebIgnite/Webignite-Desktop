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
/// The screen ruler: the frozen screen shown over the real one. Pointing measures the stretch of one colour
/// through the pointer, across and down (the gap between two things, or a block's size); dragging measures a
/// box. A click or Enter keeps the size, Esc or a right-click stops. One covers each monitor.
/// </summary>
public sealed class RulerWindow : Window
{
    private readonly TaskCompletionSource<Measured?> measured = new();
    private bool closed;

    public RulerWindow(Bitmap frozen, PixelRect bounds, double scaling, bool showHelp = true)
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
        Scaling = scaling;
        Canvas = new RulerCanvas(frozen, scaling) { Done = Finish, ShowHelp = showHelp };
        Content = Canvas;
        Closed += (_, _) =>
        {
            closed = true;
            measured.TrySetResult(null);
        };
    }

    /// <summary>How much Windows enlarges this monitor, to say what a size is at 100%.</summary>
    public double Scaling { get; }

    public RulerCanvas Canvas { get; }

    /// <summary>The size kept, or null when stopped.</summary>
    public Task<Measured?> Kept => measured.Task;

    public void Dismiss()
    {
        if (!closed)
            Close();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Activate();
        Canvas.Focus();
    }

    private void Finish(Measured? size)
    {
        measured.TrySetResult(size);
        Dismiss();
    }
}

/// <summary>Draws the frozen screen with the measuring lines; reads one row and one column of pixels each move.</summary>
public sealed class RulerCanvas : Control
{
    private readonly Bitmap frozen;
    private readonly double scaling;
    private (int X, int Y) pointer;
    private (int X, int Y)? dragFrom;
    private PixelRun across;
    private PixelRun down;
    private bool pointed;

    public RulerCanvas(Bitmap frozen, double scaling)
    {
        this.frozen = frozen;
        this.scaling = scaling;
        Focusable = true;
    }

    internal Action<Measured?>? Done { get; init; }

    internal bool ShowHelp { get; init; } = true;

    /// <summary>What a click would keep now: the box being dragged, or the stretch under the pointer.</summary>
    public Measured? Now => !pointed ? null
        : dragFrom is { } from && IsDrag(from) ? ScreenRuler.Box(from.X, from.Y, pointer.X, pointer.Y).Size
        : new Measured(across.Length, down.Length);

    private bool IsDrag((int X, int Y) from) =>
        Math.Abs(pointer.X - from.X) > ScreenRuler.ClickSlack || Math.Abs(pointer.Y - from.Y) > ScreenRuler.ClickSlack;

    // Public so the screenshot tool can drive it without a mouse.
    public void Hover(Point at)
    {
        var size = frozen.PixelSize;
        pointer = (Math.Clamp((int)(at.X * scaling), 0, size.Width - 1), Math.Clamp((int)(at.Y * scaling), 0, size.Height - 1));
        across = ScreenRuler.Reach(Read(new PixelRect(0, pointer.Y, size.Width, 1)), pointer.X);
        down = ScreenRuler.Reach(Read(new PixelRect(pointer.X, 0, 1, size.Height)), pointer.Y);
        pointed = true;
        InvalidateVisual();
    }

    public void Press(Point at)
    {
        Hover(at);
        dragFrom = pointer;
    }

    public void Release(Point at)
    {
        Hover(at);
        Done?.Invoke(Now);
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
            Press(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragFrom is not null)
            Release(e.GetPosition(this));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            Stop();
        else if (e.Key == Key.Enter && Now is not null)
            Done?.Invoke(Now);
    }

    private uint[] Read(PixelRect area)
    {
        var found = new uint[area.Width * area.Height];
        var handle = GCHandle.Alloc(found, GCHandleType.Pinned);
        try
        {
            frozen.CopyPixels(area, handle.AddrOfPinnedObject(), found.Length * 4, area.Width * 4);
        }
        finally
        {
            handle.Free();
        }
        return found;
    }

    public override void Render(DrawingContext context)
    {
        var all = new Rect(Bounds.Size);
        context.DrawImage(frozen, all);
        if (ShowHelp)
            Label(context, "Point at a gap or a block to measure it, or drag a box. Click to copy the size. Esc to stop.", new Point(all.Width / 2, 24), all, centred: true);
        if (!pointed || Now is not { } now)
            return;
        var colour = Brush("MarkBlue", Colors.DodgerBlue);
        var line = new Pen(colour, 1);
        var x = pointer.X / scaling;
        var y = pointer.Y / scaling;
        if (dragFrom is { } from && IsDrag(from))
        {
            var (left, top, size) = ScreenRuler.Box(from.X, from.Y, pointer.X, pointer.Y);
            var box = new Rect(left / scaling, top / scaling, size.Width / scaling, size.Height / scaling);
            using (context.PushOpacity(0.15))
                context.FillRectangle(colour, box);
            context.DrawRectangle(null, line, box);
            Label(context, ScreenRuler.Words(size, scaling), new Point(box.Right + 10, box.Bottom + 10), all);
            return;
        }
        // Across and down through the pointer, each as long as the colour under it runs, with a tick at both ends.
        var acrossFrom = across.Start / scaling;
        var acrossTo = (across.End + 1) / scaling;
        var downFrom = down.Start / scaling;
        var downTo = (down.End + 1) / scaling;
        context.DrawLine(line, new Point(acrossFrom, y), new Point(acrossTo, y));
        context.DrawLine(line, new Point(acrossFrom, y - 5), new Point(acrossFrom, y + 5));
        context.DrawLine(line, new Point(acrossTo, y - 5), new Point(acrossTo, y + 5));
        context.DrawLine(line, new Point(x, downFrom), new Point(x, downTo));
        context.DrawLine(line, new Point(x - 5, downFrom), new Point(x + 5, downFrom));
        context.DrawLine(line, new Point(x - 5, downTo), new Point(x + 5, downTo));
        Label(context, ScreenRuler.Words(now, scaling), new Point(x + 14, y + 14), all);
    }

    // Words on a dark plate, kept on the screen: moved back inside when they would run off an edge.
    private static void Label(DrawingContext context, string words, Point at, Rect all, bool centred = false)
    {
        var text = new FormattedText(words, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brush("NavText", Colors.White));
        var plate = new Size(text.Width + 20, text.Height + 12);
        var left = centred ? at.X - plate.Width / 2 : at.X;
        var top = at.Y;
        if (left + plate.Width > all.Width)
            left = Math.Max(0, (centred ? all.Width : at.X - 28) - plate.Width);
        if (top + plate.Height > all.Height)
            top = Math.Max(0, at.Y - 28 - plate.Height);
        context.FillRectangle(Brush("Nav", Colors.Black), new Rect(left, top, plate.Width, plate.Height), 6);
        context.DrawText(text, new Point(left + 10, top + 6));
    }

    private static IBrush Brush(string key, Color fallback) =>
        Application.Current?.TryGetResource(key, null, out var found) == true && found is IBrush brush ? brush : new SolidColorBrush(fallback);
}
