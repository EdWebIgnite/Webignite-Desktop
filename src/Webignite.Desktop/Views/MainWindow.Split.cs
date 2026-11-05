using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
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
        WireSideDrag(MainHeader, main, beside: false);
        WireSideDrag(BesideHeader, main, beside: true);
        ArrangeSplit(main);
    }

    // A side's header dragged and let go: to the other side swaps, onto the tabs makes the page beside a tab
    // again, out of the window (or well down into the page) opens it in a window of its own.
    private void WireSideDrag(Border header, MainViewModel main, bool beside)
    {
        Point? from = null;
        header.PointerPressed += (_, e) =>
        {
            // The buttons on the header do their own thing.
            if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed || (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
                return;
            from = e.GetPosition(PageArea);
            e.Pointer.Capture(header);
            header.Classes.Add("dragging");
        };
        header.PointerCaptureLost += (_, _) =>
        {
            from = null;
            header.Classes.Remove("dragging");
        };
        header.PointerReleased += (_, e) =>
        {
            if (from is not { } start)
                return;
            from = null;
            e.Pointer.Capture(null);
            header.Classes.Remove("dragging");
            if (!main.IsSplit)
                return;
            var at = e.GetPosition(PageArea);
            var onLeft = beside ? main.BesideOnLeft : !main.BesideOnLeft;
            var screen = this.PointToScreen(e.GetPosition(this));
            var drop = Webignite.Core.Layout.TabRules.DropSide(at.X, at.Y, PageArea.Bounds.Width, PageArea.Bounds.Height, start.X, start.Y,
                onLeft, main.ShowTabs && at.Y is < 0 and > -60, canBeTab: beside);
            switch (drop)
            {
                case Webignite.Core.Layout.SideDrop.Swap:
                    main.SwapSidesCommand.Execute(null);
                    break;
                case Webignite.Core.Layout.SideDrop.BackToTab:
                    main.PutBesideBack();
                    break;
                case Webignite.Core.Layout.SideDrop.PopOut:
                    var host = beside ? main.Beside : main.Current;
                    if (host is not null)
                        popOutAt[host] = screen - new PixelPoint(120, 20);
                    if (beside)
                        main.PopOutBeside();
                    else
                        main.PopOutMainSide();
                    break;
            }
            e.Handled = true;
        };
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
