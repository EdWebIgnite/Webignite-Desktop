using Avalonia;
using Avalonia.Controls;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Lays its children on a fine grid of <see cref="Columns"/> equal columns and short rows, each at the cell and
/// size set on it (<see cref="ColumnProperty"/> and the rest). The gap is only between widgets, not between
/// cells, so the grid can be fine enough to feel free while keeping edges lined up. The columns share the width,
/// so the page grows with the window; rows keep their height, and the grid is as tall as its lowest child, plus
/// <see cref="ExtraRows"/> while widgets are being moved, to drop one lower down.
/// </summary>
public sealed class WidgetGrid : Panel
{
    public const int Columns = Webignite.Core.Layout.MyPages.Columns;

    public static readonly AttachedProperty<int> ColumnProperty = AvaloniaProperty.RegisterAttached<WidgetGrid, Control, int>("Column");
    public static readonly AttachedProperty<int> RowProperty = AvaloniaProperty.RegisterAttached<WidgetGrid, Control, int>("Row");
    public static readonly AttachedProperty<int> WidthsProperty = AvaloniaProperty.RegisterAttached<WidgetGrid, Control, int>("Widths", 1);
    public static readonly AttachedProperty<int> HeightsProperty = AvaloniaProperty.RegisterAttached<WidgetGrid, Control, int>("Heights", 1);

    /// <summary>How far one row is below the last, gap included.</summary>
    public static readonly StyledProperty<double> RowPitchProperty = AvaloniaProperty.Register<WidgetGrid, double>(nameof(RowPitch), 22);
    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<WidgetGrid, double>(nameof(Gap), 16);
    public static readonly StyledProperty<int> ExtraRowsProperty = AvaloniaProperty.Register<WidgetGrid, int>(nameof(ExtraRows));

    static WidgetGrid()
    {
        AffectsParentArrange<WidgetGrid>(ColumnProperty, RowProperty, WidthsProperty, HeightsProperty);
        AffectsParentMeasure<WidgetGrid>(RowProperty, HeightsProperty, WidthsProperty);
        AffectsMeasure<WidgetGrid>(RowPitchProperty, GapProperty, ExtraRowsProperty);
    }

    public static int GetColumn(Control c) => c.GetValue(ColumnProperty);
    public static void SetColumn(Control c, int value) => c.SetValue(ColumnProperty, value);
    public static int GetRow(Control c) => c.GetValue(RowProperty);
    public static void SetRow(Control c, int value) => c.SetValue(RowProperty, value);
    public static int GetWidths(Control c) => c.GetValue(WidthsProperty);
    public static void SetWidths(Control c, int value) => c.SetValue(WidthsProperty, value);
    public static int GetHeights(Control c) => c.GetValue(HeightsProperty);
    public static void SetHeights(Control c, int value) => c.SetValue(HeightsProperty, value);

    public double RowPitch
    {
        get => GetValue(RowPitchProperty);
        set => SetValue(RowPitchProperty, value);
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

    /// <summary>How far one column is from the next: the width plus one gap, shared out, so the last widget ends at the edge.</summary>
    private double ColumnPitch(double width) => Math.Max(1, (width + Gap) / Columns);

    /// <summary>The cell under a point on the grid, for dropping a widget there.</summary>
    public (int Column, int Row) CellAt(Point point)
    {
        var column = (int)Math.Floor(point.X / ColumnPitch(Bounds.Width));
        var row = (int)Math.Floor(point.Y / RowPitch);
        return (Math.Clamp(column, 0, Columns - 1), Math.Max(0, row));
    }

    /// <summary>The cell nearest a widget's top left corner as it is dragged, for where it would land.</summary>
    public (int Column, int Row) NearestCell(Point topLeft)
    {
        var column = (int)Math.Round(topLeft.X / ColumnPitch(Bounds.Width));
        var row = (int)Math.Round(topLeft.Y / RowPitch);
        return (Math.Clamp(column, 0, Columns - 1), Math.Max(0, row));
    }

    /// <summary>How many columns and rows a stretch of pixels covers, for resizing.</summary>
    public (int Columns, int Rows) CellsFor(Size size) =>
        ((int)Math.Round((size.Width + Gap) / ColumnPitch(Bounds.Width)), (int)Math.Round((size.Height + Gap) / RowPitch));

    /// <summary>
    /// The window or pane is too narrow for the page as arranged, so the widgets are shown under each other in
    /// reading order (<see cref="Webignite.Core.Layout.MyPages.Reflow"/>). Widgets can't be moved or sized while it is so.
    /// </summary>
    public bool IsReflowed { get; private set; }

    // Where each child goes for a width: as arranged when all the columns fit, else under each other.
    private (int Columns, IReadOnlyList<(int Column, int Row, int Width, int Height)> Cells) Places(double width)
    {
        var columns = Webignite.Core.Layout.MyPages.ColumnsFor(width, Gap);
        var cells = Children.Select(c => (GetColumn(c), GetRow(c), GetWidths(c), GetHeights(c))).ToList();
        return (columns, Webignite.Core.Layout.MyPages.Reflow(cells, columns));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1000 : availableSize.Width;
        var (columns, cells) = Places(width);
        var pitch = Math.Max(1, (width + Gap) / columns);
        var rows = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var cell = cells[i];
            Children[i].Measure(new Size(Math.Max(0, pitch * cell.Width - Gap), Math.Max(0, RowPitch * cell.Height - Gap)));
            rows = Math.Max(rows, cell.Row + cell.Height);
        }
        rows += ExtraRows;
        return new Size(width, rows == 0 ? 0 : rows * RowPitch - Gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, cells) = Places(finalSize.Width);
        IsReflowed = columns < Columns;
        var pitch = Math.Max(1, (finalSize.Width + Gap) / columns);
        for (var i = 0; i < Children.Count; i++)
        {
            var cell = cells[i];
            Children[i].Arrange(new Rect(cell.Column * pitch, cell.Row * RowPitch, Math.Max(0, pitch * cell.Width - Gap), Math.Max(0, RowPitch * cell.Height - Gap)));
        }
        return finalSize;
    }
}
