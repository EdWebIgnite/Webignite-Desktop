using Avalonia;
using Avalonia.Controls;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Lays its children on a grid of <see cref="Columns"/> equal columns and rows of a fixed height, each at the
/// cell and size set on it (<see cref="ColumnProperty"/> and the rest). The columns share the width, so the page
/// grows with the window; rows keep their height, and the grid is as tall as its lowest child, plus
/// <see cref="ExtraRows"/> while widgets are being moved, to drop one lower down.
/// </summary>
public sealed class WidgetGrid : Panel
{
    public const int Columns = 12;

    public static readonly AttachedProperty<int> ColumnProperty = AvaloniaProperty.RegisterAttached<WidgetGrid, Control, int>("Column");
    public static readonly AttachedProperty<int> RowProperty = AvaloniaProperty.RegisterAttached<WidgetGrid, Control, int>("Row");
    public static readonly AttachedProperty<int> WidthsProperty = AvaloniaProperty.RegisterAttached<WidgetGrid, Control, int>("Widths", 1);
    public static readonly AttachedProperty<int> HeightsProperty = AvaloniaProperty.RegisterAttached<WidgetGrid, Control, int>("Heights", 1);

    public static readonly StyledProperty<double> RowHeightProperty = AvaloniaProperty.Register<WidgetGrid, double>(nameof(RowHeight), 72);
    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<WidgetGrid, double>(nameof(Gap), 16);
    public static readonly StyledProperty<int> ExtraRowsProperty = AvaloniaProperty.Register<WidgetGrid, int>(nameof(ExtraRows));

    static WidgetGrid()
    {
        AffectsParentArrange<WidgetGrid>(ColumnProperty, RowProperty, WidthsProperty, HeightsProperty);
        AffectsParentMeasure<WidgetGrid>(RowProperty, HeightsProperty, WidthsProperty);
        AffectsMeasure<WidgetGrid>(RowHeightProperty, GapProperty, ExtraRowsProperty);
    }

    public static int GetColumn(Control c) => c.GetValue(ColumnProperty);
    public static void SetColumn(Control c, int value) => c.SetValue(ColumnProperty, value);
    public static int GetRow(Control c) => c.GetValue(RowProperty);
    public static void SetRow(Control c, int value) => c.SetValue(RowProperty, value);
    public static int GetWidths(Control c) => c.GetValue(WidthsProperty);
    public static void SetWidths(Control c, int value) => c.SetValue(WidthsProperty, value);
    public static int GetHeights(Control c) => c.GetValue(HeightsProperty);
    public static void SetHeights(Control c, int value) => c.SetValue(HeightsProperty, value);

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public int ExtraRows
    {
        get => GetValue(ExtraRowsProperty);
        set => SetValue(ExtraRowsProperty, value);
    }

    private double ColumnWidth(double width) => Math.Max(1, (width - Gap * (Columns - 1)) / Columns);

    /// <summary>The cell under a point on the grid, for dropping a widget there.</summary>
    public (int Column, int Row) CellAt(Point point)
    {
        var column = (int)Math.Floor(point.X / (ColumnWidth(Bounds.Width) + Gap));
        var row = (int)Math.Floor(point.Y / (RowHeight + Gap));
        return (Math.Clamp(column, 0, Columns - 1), Math.Max(0, row));
    }

    /// <summary>The cell nearest a widget's top left corner as it is dragged, for where it would land.</summary>
    public (int Column, int Row) NearestCell(Point topLeft)
    {
        var column = (int)Math.Round(topLeft.X / (ColumnWidth(Bounds.Width) + Gap));
        var row = (int)Math.Round(topLeft.Y / (RowHeight + Gap));
        return (Math.Clamp(column, 0, Columns - 1), Math.Max(0, row));
    }

    /// <summary>How many columns and rows a stretch of pixels covers, for resizing.</summary>
    public (int Columns, int Rows) CellsFor(Size size) =>
        ((int)Math.Round((size.Width + Gap) / (ColumnWidth(Bounds.Width) + Gap)), (int)Math.Round((size.Height + Gap) / (RowHeight + Gap)));

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1000 : availableSize.Width;
        var column = ColumnWidth(width);
        var rows = 0;
        foreach (var child in Children)
        {
            var (w, h) = (GetWidths(child), GetHeights(child));
            child.Measure(new Size(column * w + Gap * (w - 1), RowHeight * h + Gap * (h - 1)));
            rows = Math.Max(rows, GetRow(child) + h);
        }
        rows += ExtraRows;
        return new Size(width, rows == 0 ? 0 : rows * RowHeight + (rows - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var column = ColumnWidth(finalSize.Width);
        foreach (var child in Children)
        {
            var (w, h) = (GetWidths(child), GetHeights(child));
            child.Arrange(new Rect(GetColumn(child) * (column + Gap), GetRow(child) * (RowHeight + Gap),
                column * w + Gap * (w - 1), RowHeight * h + Gap * (h - 1)));
        }
        return finalSize;
    }
}
