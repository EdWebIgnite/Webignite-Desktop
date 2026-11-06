using Avalonia;
using Avalonia.Controls;

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
