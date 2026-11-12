using Avalonia;
using Avalonia.Controls;
using Webignite.Core.Tools;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// Screenshots: the shortcut from any program, freezing the screen under the mouse, picking a part of it, and
/// opening it in Mark-up. Windows only; elsewhere the shortcut and the button do nothing.
/// </summary>
public partial class MainWindow
{
    private bool grabbing;

    private void WireScreenshots(MainViewModel main, AppServices services)
    {
        services.Go.ScreenshotAction = hideApp => TakeScreenshot(main, hideApp).Forget("Taking a screenshot", tell: true);
        OnShortcut(Webignite.Core.Tools.GlobalShortcut.Screenshot, () => services.Go.Screenshot(hideApp: false));
    }

    /// <summary>Takes the keys in Settings for the shortcut. Returns why not, when it can't.</summary>
    private async Task TakeScreenshot(MainViewModel main, bool hideApp)
    {
        if (!OperatingSystem.IsWindows() || grabbing)
            return;
        grabbing = true;
        var before = WindowState;
        var hidden = false;
        try
        {
            // From the app's own button, the app steps aside so what's behind it can be grabbed.
            if (hideApp && WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
                hidden = true;
                await Task.Delay(350);
            }
            var (x, y) = ScreenCapture.Cursor();
            if ((Screens.ScreenFromPoint(new PixelPoint(x, y)) ?? Screens.Primary) is not { } screen)
                return;
            var bounds = screen.Bounds;
            using var frozen = ScreenCapture.Grab(new ScreenGrab.Area(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            var windows = ScreenCapture.Windows(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero)
                .Select(w => w with { X = w.X - bounds.X, Y = w.Y - bounds.Y })
                .ToList();
            var picker = new ScreenGrabWindow(frozen, bounds, screen.Scaling, windows);
            picker.Show();
            if (await picker.Picked is not { } area)
                return;
            var shot = ScreenGrabWindow.Crop(frozen, area);
            main.Selected = main[Page.Markup];
            main.Markup.Show(shot);
            hidden = false;
            if (WindowState == WindowState.Minimized)
                WindowState = before == WindowState.Minimized ? WindowState.Normal : before;
            Activate();
        }
        finally
        {
            if (hidden)
                WindowState = before;
            grabbing = false;
        }
    }
}
