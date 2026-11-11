using Avalonia.Controls;

namespace Webignite.Desktop.Views;

/// <summary>The bar along the top that stands in for the sidebar's top and foot when the menu is the Menu button or the dock.</summary>
public partial class MenuBarView : UserControl
{
    public MenuBarView()
    {
        InitializeComponent();
        Looks.Attach(this, "Menu");
    }
}
