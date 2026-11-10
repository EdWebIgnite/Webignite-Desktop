using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Webignite.Desktop.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class PlannerHomeView : UserControl
{
    // The tile lists already made draggable; weak, so a widget taken off doesn't stay in memory.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> tileLists = [];

    public PlannerHomeView()
    {
        InitializeComponent();
        // Half-width widgets share the width two by two; a narrow window puts them one above the other. The new
        // width is given once this layout pass is over, and the widgets laid out again, or the boxes holding them
        // keep the old width and a wider card hangs over its neighbour (folding the sidebar did that).
        WidgetList.SizeChanged += (_, _) => Dispatcher.UIThread.Post(FitWidgets, DispatcherPriority.Render);
        DataContextChanged += (_, _) => FitWidgets();
        // A widget is dragged by its heading, before or after another, as on Asana's Home.
        DragReorder.Attach(WidgetList, new DragRules
        {
            Handle = "widgethandle",
            CanDrag = item => item is HomeWidgetRow,
            CanDrop = (item, target, _) => DataContext is PlannerHomeViewModel home && home.CanMoveWidget(item, target),
            Drop = (item, target, after) => (DataContext as PlannerHomeViewModel)?.MoveWidget(item, target, after),
            Across = _ => true,
            Label = item => (item as HomeWidgetRow)?.Title ?? "",
        });
    }

    private void FitWidgets()
    {
        if (DataContext is not PlannerHomeViewModel home)
            return;
        home.LayoutWidth = WidgetList.Bounds.Width;
        if (WidgetList.ItemsPanelRoot is not { } panel)
            return;
        foreach (var box in panel.Children)
            box.InvalidateMeasure();
        panel.InvalidateMeasure();
    }

    // The Projects widget's tiles: showing starred or all in order, a tile can be dragged before or after another.
    private void ProjectTilesLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control tiles || !tileLists.TryAdd(tiles, tiles))
            return;
        DragReorder.Attach(tiles, new DragRules
        {
            CanDrag = item => item is HomeProjectTile && DataContext is PlannerHomeViewModel { CanArrange: true },
            CanDrop = (item, target, after) => DataContext is PlannerHomeViewModel home && home.CanMoveTile(item, target, after),
            Drop = (item, target, after) => (DataContext as PlannerHomeViewModel)?.MoveTile(item, target, after),
            Across = _ => true,
            Label = item => (item as HomeProjectTile)?.Name ?? "",
        });
    }
}
