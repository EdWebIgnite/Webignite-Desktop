using Avalonia;
using Avalonia.Controls;

namespace Webignite.Desktop.Views;

/// <summary>
/// The Browser: tabs down the side, a slim address bar with the web tools at its end, each tab's page in the
/// middle (<see cref="BrowserPane"/>, which starts and stops that tab's browser engine), and the device viewer's
/// panel beside it.
/// </summary>
public partial class BrowserView : UserControl
{
    public BrowserView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AddressBox.Focus();
    }
}
