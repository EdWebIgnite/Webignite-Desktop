using Avalonia;
using Avalonia.Controls;
using Webignite.Core.Tools;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The screen ruler: a shortcut from any program (Ctrl+Alt+M) or Measure the screen in the Screen ruler tool
/// freezes every monitor (the app steps aside first when it started it), and the size pointed at or dragged is
/// copied. Windows only.
/// </summary>
public partial class MainWindow
{
    private bool measuring;

    private void WireRuler(AppServices services)
    {
        services.Go.MeasureScreenAction = measured => MeasureScreen(services, measured).Forget("Measuring the screen", tell: true);
        OnShortcut(Webignite.Core.Tools.GlobalShortcut.Ruler, () => services.Go.MeasureScreen());
    }

    private async Task MeasureScreen(AppServices services, Action<string>? measured)
    {
        if (!OperatingSystem.IsWindows() || measuring)
            return;
        measuring = true;
        var steppedAside = new List<(Window Window, WindowState Before)>();
        var rulers = new List<RulerWindow>();
        var frozen = new List<Avalonia.Media.Imaging.Bitmap>();
        try
        {
            // Started from inside the app, the app steps aside first, so what is measured is what's behind it.
            if (measured is not null || AppWindows().Any(w => w.IsActive))
            {
                foreach (var window in AppWindows().Where(w => w.IsVisible && w.WindowState != WindowState.Minimized && w.Owner is null))
                {
                    steppedAside.Add((window, window.WindowState));
                    window.WindowState = WindowState.Minimized;
                }
                if (steppedAside.Count > 0)
                    await Task.Delay(350);
            }

            var (x, y) = ScreenCapture.Cursor();
            var under = Screens.ScreenFromPoint(new PixelPoint(x, y)) ?? Screens.Primary;
            foreach (var screen in Screens.All)
            {
                var bounds = screen.Bounds;
                var picture = ScreenCapture.Grab(new ScreenGrab.Area(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                frozen.Add(picture);
                rulers.Add(new RulerWindow(picture, bounds, screen.Scaling, showHelp: under is null ? rulers.Count == 0 : bounds == under.Bounds));
            }
            if (rulers.Count == 0)
                return;
            // The one under the mouse opens last, so it has the keyboard for Enter and Esc.
            foreach (var window in rulers.OrderBy(r => r.Position == under?.Bounds.Position))
                window.Show();
            var first = await Task.WhenAny(rulers.Select(r => r.Kept));
            var result = await first;
            var scaling = rulers.FirstOrDefault(r => r.Kept == first)?.Scaling ?? 1;
            foreach (var window in rulers)
                window.Dismiss();
            rulers.Clear();
            if (result is not { } size)
                return;

            var plain = ScreenRuler.Plain(size);
            if (Clipboard is { } clipboard)
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, plain);
            var words = ScreenRuler.Words(size, scaling);
            if (measured is not null)
                measured(words);
            else
                ShowNote("Size copied", $"{words}. \"{plain}\" is on the clipboard.", null);
        }
        finally
        {
            foreach (var window in rulers)
                window.Dismiss();
            foreach (var picture in frozen)
                picture.Dispose();
            foreach (var (window, before) in steppedAside)
                window.WindowState = before;
            if (steppedAside.Count > 0 && measured is not null && toolWindows.TryGetValue(ToolKind.Ruler, out var tool))
                tool.Activate();
            else if (steppedAside.Count > 0)
                Activate();
            measuring = false;
        }
    }
}
