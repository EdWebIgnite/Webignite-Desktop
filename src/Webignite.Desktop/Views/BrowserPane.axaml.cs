using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// One tab's page in the Browser, filling the window or drawn inside a device. The page is drawn by WebView2,
/// which starts when this pane comes on screen and is shut down when it is taken off again (its tab or the
/// window closing), so nothing of the browser is left running.
/// </summary>
public partial class BrowserPane : UserControl
{
    // What a device's body adds round its screen: the bezel either side, and the status and home bars.
    private const double FrameWide = 28;
    private const double FrameHigh = 64;

    public BrowserPane()
    {
        InitializeComponent();
        // A device's screen is shrunk to fit the room there is, so the view model is told how much that is.
        Room.SizeChanged += (_, e) => (DataContext as PageCaptureToolViewModel)?.SetCaptureRoom(e.NewSize.Width - 24 - FrameWide, e.NewSize.Height - 24 - FrameHigh);
    }

    private PageCaptureToolViewModel? started;
    private bool onScreen;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        onScreen = true;
        // The device's clock says the time the viewer was opened, as a screenshot of a phone would.
        Clock.Text = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture);
        Start();
    }

    // The pane is given its tab's page just after it comes on screen, so the browser is started from whichever
    // of the two happens last.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Start();
    }

    private void Start()
    {
        if (!onScreen || DataContext is not PageCaptureToolViewModel page || ReferenceEquals(page, started))
            return;
        started?.StopCapture();
        started = page;
        page.SetCaptureRoom(Room.Bounds.Width - 24 - FrameWide, Room.Bounds.Height - 24 - FrameHigh);
        page.StartCaptureAsync().Forget("Starting the browser", tell: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        onScreen = false;
        started?.StopCapture();
        started = null;
        base.OnDetachedFromVisualTree(e);
    }
}
