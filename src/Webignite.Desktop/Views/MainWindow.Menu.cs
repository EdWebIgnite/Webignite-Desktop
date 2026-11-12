using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

// The menus that aren't the sidebar are made only while they are the one picked, so starting on the sidebar (as
// most do) builds none of them: the bar along the top and the dock when their menu is picked, the open menu the
// first time it is opened. Going back to the sidebar lets all three go.
public partial class MainWindow
{
    private void FollowMenu(MainViewModel main)
    {
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.MenuPart.HasMenuBar) or nameof(MainViewModel.MenuPart.IsDockMenu) or nameof(MainViewModel.MenuPart.IsMenuOpen) or nameof(MainViewModel.DockBarPart.DockFloats))
                ShowMenuParts(main);
        };
        ShowMenuParts(main);
    }

    private void ShowMenuParts(MainViewModel main)
    {
        if (main.MenuPart.HasMenuBar != MenuBarHost.Content is MenuBarView)
            MenuBarHost.Content = main.MenuPart.HasMenuBar ? new MenuBarView() : null;
        if (main.MenuPart.IsDockMenu != DockHost.Content is DockView)
        {
            if (DockHost.Content is DockView old)
                old.SizeChanged -= DockSized;
            var dock = main.MenuPart.IsDockMenu ? new DockView() : null;
            if (dock is not null)
                dock.SizeChanged += DockSized;
            DockHost.Content = dock;
        }
        PlaceDock(main);
        if (main.MenuPart.IsMenuOpen && MenuHost.Content is null)
            MenuHost.Content = new MenuOverlayView();
        // With the sidebar, the menu is only ever opened to choose what is on it, and let go when that is done.
        else if (main.MenuPart.IsSidebarMenu && !main.MenuPart.IsMenuOpen && MenuHost.Content is not null)
            MenuHost.Content = null;
    }

    private void DockSized(object? sender, Avalonia.Controls.SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel main)
            PlaceDock(main);
    }

    // Floating, the dock takes no room of its own: it is drawn up over the bottom of the page by its own height,
    // so the page has the window's full height under it. Otherwise it has its strip below the page.
    private void PlaceDock(MainViewModel main)
    {
        var height = main.MenuPart.IsDockMenu && main.DockBarPart.DockFloats && DockHost.Content is DockView dock ? dock.Bounds.Height : 0;
        DockHost.Margin = new Avalonia.Thickness(0, -height, 0, 0);
    }
}
