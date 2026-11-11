using Avalonia.Controls;

namespace Webignite.Desktop.Views;

/// <summary>The dock: the pages used most in a bar along the bottom, with the rest behind More.</summary>
public partial class DockView : UserControl
{
    public DockView()
    {
        InitializeComponent();
        Looks.Attach(this, "Menu");
    }
}
