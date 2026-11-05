using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Holds a page inside a scroller that scrolls both ways. Such a scroller offers endless width, so a week of
/// days or a long line would stretch past the window; this measures the page at the window's width instead,
/// so it grows and shrinks with the window and only scrolls sideways below its least width.
/// </summary>
public sealed class FitWidth : Decorator
{
    private ScrollViewer? scroller;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        scroller = this.FindAncestorOfType<ScrollViewer>();
        if (scroller is not null)
            scroller.SizeChanged += ScrollerResized;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (scroller is not null)
            scroller.SizeChanged -= ScrollerResized;
        scroller = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void ScrollerResized(object? sender, SizeChangedEventArgs e) => InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsInfinity(availableSize.Width) && scroller is { Bounds.Width: > 0 } around)
            availableSize = availableSize.WithWidth(Math.Max(around.Bounds.Width, MinWidth));
        return base.MeasureOverride(availableSize);
    }
}
