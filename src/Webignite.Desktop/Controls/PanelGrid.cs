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

    /// <summary>
    /// The page's panes, when the grid may change them itself: a hidden pane leaves a slim strip with its name
    /// to click it back, and the divider between two panes has a right-click menu to hide either one.
    /// </summary>
    public static readonly StyledProperty<ViewModels.PanelsViewModel?> PanelsProperty =
        AvaloniaProperty.Register<PanelGrid, ViewModels.PanelsViewModel?>(nameof(Panels));

    public ViewModels.PanelsViewModel? Panels
    {
        get => GetValue(PanelsProperty);
        set => SetValue(PanelsProperty, value);
    }

    /// <summary>A pane was dragged to a new width, in pixels.</summary>
    public event Action<string, double>? Resized;

    private readonly List<GridSplitter> splitters = [];
    private readonly List<Control> rails = [];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LayoutProperty || change.Property == PanelsProperty)
            Rebuild();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Rebuild();
    }

    private IEnumerable<Control> Panes => Children.Where(c => c is not GridSplitter && !rails.Contains(c) && GetPane(c) is not null);

    private List<string> shown = [];
    private bool railed;

    private void Rebuild()
    {
        var panes = Panes.ToDictionary(c => GetPane(c)!, c => c);
        var layout = Webignite.Core.Layout.Panels.Reconcile(Layout, [.. panes.Keys]);
        var visible = Webignite.Core.Layout.Panels.Visible(layout);

        // Only widths changed, as after a drag: set the columns and leave the splitters where they are.
        if (visible.SequenceEqual(shown) && railed == (Panels is not null))
        {
            foreach (var (pane, control) in panes)
            {
                if (!control.IsVisible || !layout.Widths.TryGetValue(pane, out var width))
                    continue;
                var column = ColumnDefinitions[GetColumn(control)];
                if (!column.Width.IsStar)
                    column.Width = new GridLength(width, GridUnitType.Pixel);
            }
            return;
        }
        shown = visible;
        railed = Panels is not null;

        foreach (var splitter in splitters)
            Children.Remove(splitter);
        splitters.Clear();
        foreach (var rail in rails)
            Children.Remove(rail);
        rails.Clear();

        ColumnDefinitions.Clear();
        foreach (var (pane, control) in panes)
            control.IsVisible = visible.Contains(pane);
        // A hidden pane leaves a strip where it was, to bring it back with a click.
        void Rails(IEnumerable<string> hidden)
        {
            if (Panels is not { } owner)
                return;
            foreach (var pane in hidden)
            {
                ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var rail = Rail(owner, pane);
                SetColumn(rail, ColumnDefinitions.Count - 1);
                rails.Add(rail);
                Children.Add(rail);
            }
        }
        var order = layout.Order.Where(panes.ContainsKey).ToList();
        Rails(order.TakeWhile(p => !visible.Contains(p)));
        for (var i = 0; i < visible.Count; i++)
        {
            var control = panes[visible[i]];
            var last = i == visible.Count - 1;
            // Hidden panes between this one and the next showing one.
            var after = order.SkipWhile(p => p != visible[i]).Skip(1).TakeWhile(p => !visible.Contains(p)).ToList();
            var width = layout.Widths.TryGetValue(visible[i], out var saved) ? saved : GetDefaultWidth(control);
            ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = last ? new GridLength(1, GridUnitType.Star) : new GridLength(width, GridUnitType.Pixel),
                MinWidth = Webignite.Core.Layout.Panels.MinWidth,
            });
            SetColumn(control, ColumnDefinitions.Count - 1);
            if (last)
            {
                Rails(after);
                continue;
            }
            if (after.Count > 0)
            {
                // A strip in between stands in for the divider: the pane before keeps its width.
                Rails(after);
                continue;
            }
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
            if (Panels is { } panels)
                splitter.ContextMenu = DividerMenu(panels, visible[i], visible[i + 1]);
            SetColumn(splitter, ColumnDefinitions.Count - 1);
            splitter.DragCompleted += (_, _) => ReportWidths();
            splitters.Add(splitter);
            Children.Add(splitter);
        }
    }

    private static Button Rail(ViewModels.PanelsViewModel panels, string pane)
    {
        var title = panels.TitleOf(pane);
        var rail = new Button
        {
            Classes = { "panerail" },
            Content = new LayoutTransformControl
            {
                LayoutTransform = new Avalonia.Media.RotateTransform(90),
                Child = new TextBlock { Text = title, FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            },
        };
        ToolTip.SetTip(rail, $"Show {title}");
        rail.Click += (_, _) => panels.Show(pane, true);
        return rail;
    }

    private static ContextMenu DividerMenu(ViewModels.PanelsViewModel panels, string left, string right)
    {
        var menu = new ContextMenu();
        foreach (var pane in new[] { left, right })
        {
            var hide = new MenuItem { Header = $"Hide {panels.TitleOf(pane)}", IsEnabled = panels.CanHide(pane) };
            hide.Click += (_, _) => panels.Show(pane, false);
            menu.Items.Add(hide);
        }
        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = "Put the panes back" };
        reset.Click += (_, _) => panels.Reset();
        menu.Items.Add(reset);
        return menu;
    }

    // Every pane with a pixel column: the last one is whatever is left, so it has no width to remember.
    // The widths are gathered first: reporting one changes the layout, which must not happen mid-walk.
    private void ReportWidths()
    {
        var widths = Panes.Where(c => c.IsVisible)
            .Select(c => (Pane: GetPane(c)!, Column: ColumnDefinitions[GetColumn(c)]))
            .Where(p => !p.Column.Width.IsStar)
            .Select(p => (p.Pane, p.Column.ActualWidth))
            .ToList();
        foreach (var (pane, width) in widths)
            Resized?.Invoke(pane, width);
    }
}
