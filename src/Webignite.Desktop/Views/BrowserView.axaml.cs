using Avalonia;
using Avalonia.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The Browser tool: a site to look at and click through, at the window's size or a phone's, tablet's or
/// computer's. The page is drawn by WebView2, which starts when this is shown in its window and is shut down when
/// it is taken off again, so nothing of the browser is left running.
/// </summary>
public partial class BrowserView : UserControl
{
    public BrowserView()
    {
        InitializeComponent();
        // A device's screen is shrunk to fit the room there is, so the view model is told how much that is.
        Room.SizeChanged += (_, e) => (DataContext as PageCaptureToolViewModel)?.SetCaptureRoom(e.NewSize.Width - 24, e.NewSize.Height - 24);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as PageCaptureToolViewModel)?.StartCaptureAsync().Forget("Starting the browser", tell: true);
        AddressBox.Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as PageCaptureToolViewModel)?.StopCapture();
        base.OnDetachedFromVisualTree(e);
    }
}
