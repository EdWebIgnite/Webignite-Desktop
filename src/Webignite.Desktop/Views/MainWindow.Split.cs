using Avalonia.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// Side by side: lays the main page and the page beside it out in the page area, on the side asked for, at the
/// width last chosen, and keeps that width when the divider is dragged.
/// </summary>
public partial class MainWindow
{
    private void WireSplit(MainViewModel main)
    {
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Beside) or nameof(MainViewModel.BesideOnLeft))
                ArrangeSplit(main);
        };
        Divider.DragCompleted += (_, _) => KeepShare(main);
        ArrangeSplit(main);
    }

    private void ArrangeSplit(MainViewModel main)
    {
        var columns = PageArea.ColumnDefinitions;
        if (!main.IsSplit)
        {
            Grid.SetColumn(MainPane, 0);
            columns[0].Width = new GridLength(1, GridUnitType.Star);
            columns[0].MinWidth = 0;
            columns[2].MinWidth = 0;
            columns[1].Width = new GridLength(0);
            columns[2].Width = new GridLength(0);
            return;
        }
        var share = main.MainShare;
        var (mainColumn, besideColumn) = main.BesideOnLeft ? (2, 0) : (0, 2);
        Grid.SetColumn(MainPane, mainColumn);
        Grid.SetColumn(BesidePane, besideColumn);
        columns[mainColumn].Width = new GridLength(share, GridUnitType.Star);
        columns[besideColumn].Width = new GridLength(1 - share, GridUnitType.Star);
        // Neither side can be dragged narrower than a page is usable at; past that it scrolls sideways.
        columns[mainColumn].MinWidth = 360;
        columns[besideColumn].MinWidth = 360;
        columns[1].Width = GridLength.Auto;
    }

    private void KeepShare(MainViewModel main)
    {
        var total = MainPane.Bounds.Width + BesidePane.Bounds.Width;
        if (total > 0)
            main.MainShare = MainPane.Bounds.Width / total;
        ArrangeSplit(main);
    }
}
