using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Webignite.Core.Layout;
using Webignite.Desktop.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class MyPagesView : UserControl
{
    public static readonly IValueConverter Faded = new FuncValueConverter<bool, double>(other => other ? 0.4 : 1);
    public static readonly IValueConverter Bold = new FuncValueConverter<bool, FontWeight>(today => today ? FontWeight.Bold : FontWeight.Normal);
    public static readonly IValueConverter MoveCursor = new FuncValueConverter<bool, Cursor?>(arranging => arranging ? new Cursor(StandardCursorType.SizeAll) : null);

    private WidgetViewModel? held;
    private bool sizing;
    private Point grabbed;
    private Size startSize;

    public MyPagesView()
    {
        InitializeComponent();
        Board.AddHandler(PointerMovedEvent, Dragged, RoutingStrategies.Tunnel, handledEventsToo: true);
        Board.AddHandler(PointerReleasedEvent, Dropped, RoutingStrategies.Tunnel, handledEventsToo: true);
        Board.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble);
    }

    private MyPagesViewModel? Pages => DataContext as MyPagesViewModel;

    private WidgetGrid? Grid => Board.ItemsPanelRoot as WidgetGrid;

    // Picking up a widget by its heading while arranging.
    private void HeadingPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Pages is not { IsEditing: true } pages || (sender as Control)?.DataContext is not WidgetViewModel widget
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Grid is null)
            return;
        if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        var card = (sender as Visual)?.FindAncestorOfType<ContentPresenter>();
        if (card is null)
            return;
        held = widget;
        sizing = false;
        grabbed = e.GetPosition(card);
        pages.BeginArranging();
        e.Pointer.Capture(Board);
        e.Handled = true;
    }

    // Taking hold of a widget's corner to size it.
    private void CornerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Pages is not { IsEditing: true } pages || (sender as Control)?.DataContext is not WidgetViewModel widget || Grid is null)
            return;
        var card = (sender as Visual)?.FindAncestorOfType<ContentPresenter>();
        if (card is null)
            return;
        held = widget;
        sizing = true;
        grabbed = e.GetPosition(Grid);
        startSize = card.Bounds.Size;
        pages.BeginArranging();
        e.Pointer.Capture(Board);
        e.Handled = true;
    }

    private void Dragged(object? sender, PointerEventArgs e)
    {
        if (held is null || Pages is not { } pages || Grid is not { } grid)
            return;
        var at = e.GetPosition(grid);
        if (sizing)
        {
            var size = new Size(Math.Max(1, startSize.Width + at.X - grabbed.X), Math.Max(1, startSize.Height + at.Y - grabbed.Y));
            var (columns, rows) = grid.CellsFor(size);
            if (columns != held.Width || rows != held.Height)
                pages.ResizeTo(held, columns, rows);
        }
        else
        {
            var (column, row) = grid.NearestCell(at - grabbed);
            if (column != held.Column || row != held.Row)
                pages.MoveTo(held, column, row);
        }
    }

    private void Dropped(object? sender, PointerReleasedEventArgs e)
    {
        if (held is null)
            return;
        e.Pointer.Capture(null);
        EndDrag();
        e.Handled = true;
    }

    private void EndDrag()
    {
        if (held is null)
            return;
        held = null;
        Pages?.EndArranging();
    }

    /// <summary>The kinds of widget to add, with a project or client to pick for the ones that show one.</summary>
    private void AddOpening(object? sender, EventArgs e)
    {
        if (Pages is not { } pages || sender is not MenuFlyout flyout)
            return;
        flyout.Items.Clear();
        foreach (var kind in Enum.GetValues<WidgetKind>())
        {
            var item = new MenuItem { Header = MyPages.Label(kind) };
            if (kind == WidgetKind.Project)
            {
                foreach (var (id, name) in pages.ProjectChoices())
                {
                    var pick = new MenuItem { Header = name };
                    pick.Click += (_, _) => pages.AddWidget(kind, id);
                    item.Items.Add(pick);
                }
                item.IsEnabled = item.Items.Count > 0;
            }
            else if (kind == WidgetKind.Client)
            {
                foreach (var (id, name) in pages.ClientChoices())
                {
                    var pick = new MenuItem { Header = name };
                    pick.Click += (_, _) => pages.AddWidget(kind, id);
                    item.Items.Add(pick);
                }
                item.IsEnabled = item.Items.Count > 0;
            }
            else
                item.Click += (_, _) => pages.AddWidget(kind);
            flyout.Items.Add(item);
            if (kind is WidgetKind.Client or WidgetKind.Note)
                flyout.Items.Add(new Separator());
        }
    }
}
