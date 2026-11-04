using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Webignite.Core.Layout;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The main window's part in popping pages out: it makes a window for each page in <see cref="MainViewModel.PoppedOut"/>
/// and closes it when the page goes back, lets a tab be dragged out of the strip, and says whether a point on
/// screen is over its tabs (where a popped-out window's header can be let go to put the page back).
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<PageHost, PageWindow> popOuts = [];
    private readonly Dictionary<PageHost, PixelPoint> popOutAt = [];
    private PageHost? draggingTab;
    private Point dragStart;
    private bool closingAll;

    /// <summary>The popped-out windows open now, for the screenshot and memory checks.</summary>
    public IReadOnlyCollection<PageWindow> PopOutWindows => popOuts.Values;

    private void WirePopOuts(MainViewModel main)
    {
        main.PoppedOut.CollectionChanged += (_, e) => PoppedOutChanged(main, e);
        TabStrip.AddHandler(PointerPressedEvent, TabPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        TabStrip.AddHandler(PointerReleasedEvent, TabLetGo, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void PoppedOutChanged(MainViewModel main, NotifyCollectionChangedEventArgs e)
    {
        foreach (var host in e.NewItems?.OfType<PageHost>() ?? [])
        {
            var window = new PageWindow(host, main, OverTabs);
            window.Classes.Set("motion-full", Classes.Contains("motion-full"));
            window.Classes.Set("motion-reduced", Classes.Contains("motion-reduced"));
            if (popOutAt.Remove(host, out var at))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = at;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = Position + new PixelPoint(80, 60);
            }
            popOuts[host] = window;
            window.Show();
        }
        foreach (var host in e.OldItems?.OfType<PageHost>() ?? [])
        {
            if (!popOuts.Remove(host, out var window) || window.IsClosed)
                continue;
            window.ClosingQuietly = true;
            window.Close();
        }
    }

    /// <summary>Whether a point on screen is over the top of the main window's page area, where the tabs are.</summary>
    private bool OverTabs(PixelPoint screen)
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
            return false;
        var local = this.PointToClient(screen);
        var page = FullHost.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
        var top = TabStrip.IsEffectivelyVisible ? (TabStrip.TranslatePoint(new Point(0, 0), this)?.Y ?? 0) : page.Y;
        return local.X >= page.X && local.X <= Bounds.Width && local.Y >= top - 8 && local.Y <= top + 56;
    }

    // A tab dragged down out of the strip, or out of the window, and let go becomes a window of its own there.
    private void TabPressed(object? sender, PointerPressedEventArgs e)
    {
        draggingTab = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            ? (e.Source as StyledElement)?.DataContext as PageHost
            : null;
        dragStart = e.GetPosition(this);
    }

    private void TabLetGo(object? sender, PointerReleasedEventArgs e)
    {
        if (draggingTab is not { } tab || DataContext is not MainViewModel main)
            return;
        draggingTab = null;
        var at = e.GetPosition(this);
        var strip = TabStrip.TranslatePoint(new Point(0, TabStrip.Bounds.Height), this)?.Y ?? 0;
        var outside = at.X < 0 || at.Y < 0 || at.X > Bounds.Width || at.Y > Bounds.Height;
        if (!outside && at.Y < strip + 60)
            return;
        if (Math.Abs(at.Y - dragStart.Y) + Math.Abs(at.X - dragStart.X) < 30)
            return;
        popOutAt[tab] = this.PointToScreen(at) - new PixelPoint(120, 20);
        main.PopOut(tab);
        e.Handled = true;
    }

    /// <summary>Where each popped-out window is, for saving with the tabs.</summary>
    private PopOutPlacement? Placement(PageHost host) =>
        popOuts.TryGetValue(host, out var window)
            ? new PopOutPlacement(host.Key.ToString(), window.Position.X, window.Position.Y, window.Width, window.Height, window.WindowState == WindowState.Maximized)
            : null;

    /// <summary>Opens the popped-out windows from last time where they were.</summary>
    private void RestorePopOuts(MainViewModel main, TabSettings saved)
    {
        // Each window is made as its page pops out; the size and place are put on before it first shows.
        foreach (var (host, where) in main.RestorePoppedOut(saved))
        {
            if (!popOuts.TryGetValue(host, out var window))
                continue;
            window.Position = new PixelPoint(where.X, where.Y);
            window.Width = Math.Max(window.MinWidth, where.Width);
            window.Height = Math.Max(window.MinHeight, where.Height);
            if (where.Maximized)
                window.WindowState = WindowState.Maximized;
            BringOnScreen(window);
        }
    }

    // A screen that was there last time may be unplugged now: bring the window back into view.
    private static void BringOnScreen(Window window)
    {
        if (window.Screens.ScreenFromPoint(window.Position + new PixelPoint(40, 20)) is null && window.Screens.Primary is { } screen)
            window.Position = new PixelPoint(screen.WorkingArea.X + 40, screen.WorkingArea.Y + 40);
    }

    /// <summary>When the app closes, the popped-out windows close with it and stay listed to come back next time.</summary>
    private void ClosePopOutsQuietly()
    {
        if (closingAll)
            return;
        closingAll = true;
        foreach (var window in popOuts.Values.ToList())
        {
            window.ClosingQuietly = true;
            window.Close();
        }
    }
}
