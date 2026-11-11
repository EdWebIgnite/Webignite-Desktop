using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// Page to PDF or picture. The page is drawn by WebView2, which starts when this is shown in its window and is
/// shut down when it is taken off again, so nothing of the browser is left running.
/// </summary>
public partial class PageCaptureView : UserControl
{
    public PageCaptureView()
    {
        InitializeComponent();
        // A device's screen is shrunk to fit the room there is, so the view model is told how much that is.
        Room.SizeChanged += (_, e) => (DataContext as ToolsViewModel)?.SetCaptureRoom(e.NewSize.Width - 24, e.NewSize.Height - 24);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as ToolsViewModel)?.StartCaptureAsync().Forget("Starting the browser", tell: true);
        AddressBox.Focus();
    }

    private void ShowInFiles(object? sender, RoutedEventArgs e)
    {
        (DataContext as ToolsViewModel)?.ShowCapturedCommand.Execute(null);
        MainToFront();
    }

    private void OpenInMarkup(object? sender, RoutedEventArgs e) => MarkUpAsync().Forget("Opening a page in Mark-up", tell: true);

    private async Task MarkUpAsync()
    {
        if (DataContext is not ToolsViewModel tools)
            return;
        await tools.MarkUpPageCommand.ExecuteAsync(null);
        if (!tools.CaptureProblem)
            MainToFront();
    }

    // What was saved is shown in the main window, which may be behind this one.
    private static void MainToFront()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main })
            main.Activate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as ToolsViewModel)?.StopCapture();
        base.OnDetachedFromVisualTree(e);
    }
}
