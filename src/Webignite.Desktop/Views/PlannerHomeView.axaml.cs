using Avalonia;
using Avalonia.Controls;
using Webignite.Desktop.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class PlannerHomeView : UserControl
{
    /// <summary>From this width My tasks and Projects sit side by side instead of one above the other.</summary>
    private const double SideBySide = 900;

    private bool? wide;

    public PlannerHomeView()
    {
        InitializeComponent();
        Cards.SizeChanged += (_, _) => Arrange();
        // Showing starred or all in order, a project's tile can be dragged before or after another.
        DragReorder.Attach(ProjectTiles, new DragRules
        {
            CanDrag = item => item is HomeProjectTile && DataContext is PlannerHomeViewModel { CanArrange: true },
            CanDrop = (item, target, after) => DataContext is PlannerHomeViewModel home && home.CanMoveTile(item, target, after),
            Drop = (item, target, after) => (DataContext as PlannerHomeViewModel)?.MoveTile(item, target, after),
            Across = _ => true,
            Label = item => (item as HomeProjectTile)?.Name ?? "",
        });
    }

    private void Arrange()
    {
        var nowWide = Cards.Bounds.Width >= SideBySide;
        if (nowWide == wide)
            return;
        wide = nowWide;
        Cards.ColumnDefinitions = nowWide ? new ColumnDefinitions("*,24,*") : new ColumnDefinitions("*");
        Grid.SetColumn(ProjectsCard, nowWide ? 2 : 0);
        Grid.SetRow(ProjectsCard, nowWide ? 0 : 1);
        ProjectsCard.Margin = nowWide ? default : new Thickness(0, 24, 0, 0);
    }
}
