using Avalonia;
using Avalonia.Controls;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A row that shows only the children that fit whole. Beside the dock there may be room for all of the person's
/// things or, on a small window, for one or none; a clock cut off halfway reads as a fault, so what doesn't fit
/// is left out until the window is wider. The first children are the ones kept.
/// </summary>
public sealed class FitRowPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<FitRowPanel, double>(nameof(Spacing));

    static FitRowPanel() => AffectsMeasure<FitRowPanel>(SpacingProperty);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private int fitting;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        fitting = 0;
        var full = false;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var wanted = child.DesiredSize.Width + (fitting > 0 ? Spacing : 0);
            if (full || width + wanted > availableSize.Width)
            {
                full = true; // nothing after the first that doesn't fit, so the order stays the person's
                continue;
            }
            width += wanted;
            height = Math.Max(height, child.DesiredSize.Height);
            fitting++;
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            // One that doesn't fit is given no room, and is also made unseen and unreachable: with no room it would
            // still draw over its neighbours and take the Tab key.
            var fits = i < fitting;
            child.Opacity = fits ? 1 : 0;
            child.IsEnabled = fits;
            if (!fits)
            {
                child.Arrange(default);
                continue;
            }
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width + Spacing;
        }
        return finalSize;
    }
}
