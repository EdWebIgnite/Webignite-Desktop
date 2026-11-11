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
            if (e.PropertyName is nameof(MainViewModel.HasMenuBar) or nameof(MainViewModel.IsDockMenu) or nameof(MainViewModel.IsMenuOpen))
                ShowMenuParts(main);
        };
        ShowMenuParts(main);
    }

    private void ShowMenuParts(MainViewModel main)
    {
        if (main.HasMenuBar != MenuBarHost.Content is MenuBarView)
            MenuBarHost.Content = main.HasMenuBar ? new MenuBarView() : null;
        if (main.IsDockMenu != DockHost.Content is DockView)
            DockHost.Content = main.IsDockMenu ? new DockView() : null;
        if (main.IsMenuOpen && MenuHost.Content is null)
            MenuHost.Content = new MenuOverlayView();
        else if (main.IsSidebarMenu && MenuHost.Content is not null)
            MenuHost.Content = null;
    }
}
