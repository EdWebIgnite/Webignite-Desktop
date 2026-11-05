using Avalonia;
using Avalonia.Controls;

namespace Webignite.Desktop.Views;

public partial class OverviewView : UserControl
{
    /// <summary>From this width Needs you and Today sit side by side instead of one above the other.</summary>
    private const double SideBySide = 1300;

    private bool? wide;

    public OverviewView()
    {
        InitializeComponent();
        Lower.SizeChanged += (_, _) => Arrange();
        NeedsCard.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                wide = null;
                Arrange();
            }
        };
    }

    private void Arrange()
    {
        var nowWide = Lower.Bounds.Width >= SideBySide && NeedsCard.IsVisible;
        if (nowWide == wide)
            return;
        wide = nowWide;
        Lower.ColumnDefinitions = nowWide ? new ColumnDefinitions("3*,22,2*") : new ColumnDefinitions("*");
        Grid.SetColumn(TodayCard, nowWide ? 2 : 0);
        Grid.SetRow(TodayCard, nowWide ? 0 : 1);
        TodayCard.Margin = nowWide || !NeedsCard.IsVisible ? default : new Thickness(0, 22, 0, 0);
    }
}
