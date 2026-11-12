using Avalonia;
using Avalonia.Controls;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Lays its children down columns of one width, in order, using as many columns as the width has room for and
/// making them as short as that allows. The open menu uses it: on a wide window its groups spread sideways and
/// the menu is low, on a narrow one they stack and the menu scrolls. A child is never split over two columns.
/// </summary>
public sealed class ColumnsPanel : Panel
{
    public static readonly StyledProperty<double> ColumnWidthProperty = AvaloniaProperty.Register<ColumnsPanel, double>(nameof(ColumnWidth), 220);
    public static readonly StyledProperty<double> ColumnSpacingProperty = AvaloniaProperty.Register<ColumnsPanel, double>(nameof(ColumnSpacing));
    public static readonly StyledProperty<double> ItemSpacingProperty = AvaloniaProperty.Register<ColumnsPanel, double>(nameof(ItemSpacing));

    static ColumnsPanel() => AffectsMeasure<ColumnsPanel>(ColumnWidthProperty, ColumnSpacingProperty, ItemSpacingProperty);

    public double ColumnWidth
    {
        get => GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    // Which column each child is in, and how far down it; worked out when measuring.
    private readonly List<(int Column, double Top)> places = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var heights = new List<double>(Children.Count);
        foreach (var child in Children)
        {
            child.Measure(new Size(ColumnWidth, double.PositiveInfinity));
            heights.Add(child.IsVisible ? child.DesiredSize.Height : 0);
        }
        var most = double.IsInfinity(availableSize.Width)
            ? Math.Max(1, heights.Count)
            : Math.Max(1, (int)((availableSize.Width + ColumnSpacing) / (ColumnWidth + ColumnSpacing)));
        var height = ColumnLayout.ShortestHeight(heights, most, ItemSpacing);
        places.Clear();
        places.AddRange(ColumnLayout.Place(heights, height, ItemSpacing));
        var used = places.Count == 0 ? 0 : places.Max(p => p.Column) + 1;
        return new Size(used == 0 ? 0 : used * ColumnWidth + (used - 1) * ColumnSpacing, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < Children.Count && i < places.Count; i++)
        {
            var (column, top) = places[i];
            Children[i].Arrange(new Rect(column * (ColumnWidth + ColumnSpacing), top, ColumnWidth, Children[i].DesiredSize.Height));
        }
        return finalSize;
    }
}

/// <summary>The sums behind <see cref="ColumnsPanel"/>, apart from it so they can be tested without a window.</summary>
public static class ColumnLayout
{
    /// <summary>Where each thing goes when columns are filled in order and none may be taller than <paramref name="height"/>.</summary>
    public static IReadOnlyList<(int Column, double Top)> Place(IReadOnlyList<double> heights, double height, double spacing)
    {
        var places = new List<(int, double)>(heights.Count);
        var column = 0;
        double top = 0;
        foreach (var h in heights)
        {
            // One taller than the column still gets a column to itself, as nothing is split.
            if (top > 0 && top + h > height + 0.5)
            {
                column++;
                top = 0;
            }
            places.Add((column, top));
            if (h > 0)
                top += h + spacing;
        }
        return places;
    }

    /// <summary>The lowest height at which everything, kept in order, fits in <paramref name="columns"/> columns.</summary>
    public static double ShortestHeight(IReadOnlyList<double> heights, int columns, double spacing)
    {
        if (heights.Count == 0)
            return 0;
        // The answer is always some run of neighbours stacked in one column, so only those heights are tried.
        var candidates = new SortedSet<double>();
        for (var from = 0; from < heights.Count; from++)
        {
            double run = 0;
            for (var to = from; to < heights.Count; to++)
            {
                if (heights[to] <= 0)
                    continue;
                run += (run > 0 ? spacing : 0) + heights[to];
                candidates.Add(run);
            }
        }
        foreach (var candidate in candidates)
            if (candidate >= heights.Max() && Place(heights, candidate, spacing).Max(p => p.Column) < columns)
                return candidate;
        return candidates.Count == 0 ? 0 : candidates.Max;
    }
}
