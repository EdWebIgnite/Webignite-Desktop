using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// One page in a window of its own. Dragging its header moves the window; letting go over the main window's
/// tabs puts the page back there. Closing the window closes the page, or puts it back when it has work open.
/// </summary>
public partial class PageWindow : Window
{
    private readonly MainViewModel? main;
    private readonly Func<PixelPoint, bool>? overMainTabs;
    private PixelPoint dragFrom;
    private PixelPoint windowFrom;
    private bool dragging;

    /// <summary>Set when the window is closed because the page went back or the app is closing, not by the person.</summary>
    internal bool ClosingQuietly { get; set; }

    /// <summary>Already closed, so it isn't closed a second time when its page is let go.</summary>
    internal bool IsClosed { get; private set; }

    public PageWindow()
    {
        InitializeComponent();
    }

    public PageWindow(PageHost host, MainViewModel main, Func<PixelPoint, bool> overMainTabs) : this()
    {
        DataContext = host;
        this.main = main;
        this.overMainTabs = overMainTabs;
        Header.PointerPressed += HeaderPressed;
        Header.PointerMoved += HeaderMoved;
        Header.PointerReleased += HeaderReleased;
    }

    public PageHost? Host => DataContext as PageHost;

    // Ctrl+W closes this window, as it closes a tab in the main one.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.W && e.KeyModifiers == KeyModifiers.Control)
        {
            Close();
            e.Handled = true;
        }
    }

    private void PutBack(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host)
            main?.DockBack(host);
    }

    // The header moves the window by hand, so where it is let go is known and can put the page back.
    private void HeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || WindowState != WindowState.Normal)
            return;
        dragging = true;
        dragFrom = this.PointToScreen(e.GetPosition(this));
        windowFrom = Position;
        e.Pointer.Capture(Header);
    }

    private void HeaderMoved(object? sender, PointerEventArgs e)
    {
        if (!dragging)
            return;
        var now = this.PointToScreen(e.GetPosition(this));
        Position = windowFrom + (now - dragFrom);
    }

    private void HeaderReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!dragging)
            return;
        dragging = false;
        e.Pointer.Capture(null);
        var at = this.PointToScreen(e.GetPosition(this));
        if (Host is { } host && overMainTabs?.Invoke(at) == true)
            main?.DockBack(host);
    }

    protected override void OnClosed(EventArgs e)
    {
        IsClosed = true;
        base.OnClosed(e);
        if (!ClosingQuietly && Host is { } host)
            main?.PoppedOutClosed(host);
        DataContext = null;
    }
}
