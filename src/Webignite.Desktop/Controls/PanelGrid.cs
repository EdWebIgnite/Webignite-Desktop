using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Webignite.Core.Layout;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A row of panes the person can drag wider or narrower, reorder and hide. Each child names its pane with
/// <c>PanelGrid.Pane="folders"</c> and its usual width with <c>PanelGrid.DefaultWidth</c>; the last pane
/// showing takes what is left. The <see cref="Layout"/> says the order, widths and what is hidden, and
/// <see cref="Resized"/> reports a drag so the view model can remember it.
/// </summary>
public sealed class PanelGrid : Grid
{
    public static readonly AttachedProperty<string?> PaneProperty =
        AvaloniaProperty.RegisterAttached<PanelGrid, Control, string?>("Pane");

    public static readonly AttachedProperty<double> DefaultWidthProperty =
        AvaloniaProperty.RegisterAttached<PanelGrid, Control, double>("DefaultWidth", 300);

    public static readonly StyledProperty<PanelLayout?> LayoutProperty =
        AvaloniaProperty.Register<PanelGrid, PanelLayout?>(nameof(Layout));

    public static string? GetPane(Control c) => c.GetValue(PaneProperty);
    public static void SetPane(Control c, string? value) => c.SetValue(PaneProperty, value);
    public static double GetDefaultWidth(Control c) => c.GetValue(DefaultWidthProperty);
    public static void SetDefaultWidth(Control c, double value) => c.SetValue(DefaultWidthProperty, value);

    public PanelLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>A pane was dragged to a new width, in pixels.</summary>
    public event Action<string, double>? Resized;

    private readonly List<GridSplitter> splitters = [];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LayoutProperty)
            Rebuild();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Rebuild();
    }

    private IEnumerable<Control> Panes => Children.Where(c => c is not GridSplitter && GetPane(c) is not null);

    private void Rebuild()
    {
        foreach (var splitter in splitters)
            Children.Remove(splitter);
        splitters.Clear();

        var panes = Panes.ToDictionary(c => GetPane(c)!, c => c);
        var layout = Panels.Reconcile(Layout, [.. panes.Keys]);
        var visible = Panels.Visible(layout);

        ColumnDefinitions.Clear();
        foreach (var (pane, control) in panes)
            control.IsVisible = visible.Contains(pane);
        for (var i = 0; i < visible.Count; i++)
        {
            var control = panes[visible[i]];
            var last = i == visible.Count - 1;
            var width = layout.Widths.TryGetValue(visible[i], out var saved) ? saved : GetDefaultWidth(control);
            ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = last ? new GridLength(1, GridUnitType.Star) : new GridLength(width, GridUnitType.Pixel),
                MinWidth = Panels.MinWidth,
            });
            SetColumn(control, ColumnDefinitions.Count - 1);
            if (last)
                continue;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Auto) });
            var splitter = new GridSplitter
            {
                Width = 6,
                Margin = new Thickness(-3, 0),
                ZIndex = 10,
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            };
            splitter.Classes.Add("panes");
            SetColumn(splitter, ColumnDefinitions.Count - 1);
            splitter.DragCompleted += (_, _) => ReportWidths();
            splitters.Add(splitter);
            Children.Add(splitter);
        }
    }

    // Every pane with a pixel column: the last one is whatever is left, so it has no width to remember.
    private void ReportWidths()
    {
        foreach (var control in Panes.Where(c => c.IsVisible))
        {
            var column = ColumnDefinitions[GetColumn(control)];
            if (!column.Width.IsStar)
                Resized?.Invoke(GetPane(control)!, column.ActualWidth);
        }
    }
}
