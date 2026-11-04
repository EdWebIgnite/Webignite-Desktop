using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Webignite.Desktop.Views;

/// <summary>
/// A page that fills the window. Such pages sit in a sideways scroller, which offers endless width; this keeps
/// to the width on screen instead, so long names trim and lists wrap, and the scroller only scrolls below the
/// page's least width.
/// </summary>
public class FullBleedView : UserControl
{
    private const double LeastWidth = 760;
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
            availableSize = availableSize.WithWidth(Math.Max(around.Bounds.Width, LeastWidth));
        return base.MeasureOverride(availableSize);
    }
}
