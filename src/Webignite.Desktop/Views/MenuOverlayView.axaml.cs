using Avalonia.Controls;
using Avalonia.Input;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>The whole menu, opened over the page by the Menu button, the dock's More or Ctrl+M.</summary>
public partial class MenuOverlayView : UserControl
{
    public MenuOverlayView()
    {
        InitializeComponent();
        Looks.Attach(this, "Menu");
    }

    // A click on the dimmed page behind the menu closes it; a click inside it doesn't reach the page.
    private void CloseMenu(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel main)
            main.IsMenuOpen = false;
    }

    private void KeepMenuOpen(object? sender, PointerPressedEventArgs e) => e.Handled = true;
}
