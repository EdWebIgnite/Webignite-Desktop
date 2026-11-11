using Avalonia;
using Avalonia.Controls;
using Webignite.Core.Tools;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The colour picker: a shortcut from any program (Ctrl+Alt+C) or Pick from screen in the Colours tool freezes
/// every monitor (the app steps aside first when it started the pick), and the colour clicked is copied as its code. Windows only.
/// </summary>
public partial class MainWindow
{
    private GlobalHotkey? colourKey;
    private bool pickingColour;

    private void WireColourPicker(AppServices services)
    {
        services.Go.PickColourAction = picked => PickColour(services, picked).Forget("Picking a colour", tell: true);
        services.ApplyColourShortcut = () => ApplyColourShortcut(services);
        Opened += (_, _) =>
        {
            if (!OperatingSystem.IsWindows())
                return;
            colourKey = new GlobalHotkey(this, 0x574A, () => services.Go.PickColour(), "Screenshots");
            if (ApplyColourShortcut(services) is { } problem)
                ShowNote("Colour picker shortcut not set", problem, () => services.Go.Settings("screenshots"));
        };
        Closed += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
                colourKey?.Release();
            colourKey = null;
        };
    }

    private string? ApplyColourShortcut(AppServices services)
    {
        if (!OperatingSystem.IsWindows() || colourKey is null)
            return null;
        var shortcut = services.Settings.Screenshots.ColourShortcut;
        if (string.IsNullOrWhiteSpace(shortcut))
            return colourKey.Set(null);
        var (keys, problem) = Hotkey.Parse(shortcut);
        return problem ?? colourKey.Set(keys);
    }

    private async Task PickColour(AppServices services, Action<string>? picked)
    {
        if (!OperatingSystem.IsWindows() || pickingColour)
            return;
        pickingColour = true;
        var steppedAside = new List<(Window Window, WindowState Before)>();
        var pickers = new List<ColourPickWindow>();
        var frozen = new List<Avalonia.Media.Imaging.Bitmap>();
        try
        {
            // Started from inside the app, the app steps aside first, so the frozen screen shows what's behind it,
            // such as a website, and not Webignite itself.
            if (picked is not null || AppWindows().Any(w => w.IsActive))
            {
                foreach (var window in AppWindows().Where(w => w.IsVisible && w.WindowState != WindowState.Minimized && w.Owner is null))
                {
                    steppedAside.Add((window, window.WindowState));
                    window.WindowState = WindowState.Minimized;
                }
                if (steppedAside.Count > 0)
                    await Task.Delay(350);
            }

            // Every monitor is frozen, each under its own picker, so a colour can come from any of them.
            var (x, y) = ScreenCapture.Cursor();
            var under = Screens.ScreenFromPoint(new PixelPoint(x, y)) ?? Screens.Primary;
            foreach (var screen in Screens.All)
            {
                var bounds = screen.Bounds;
                var picture = ScreenCapture.Grab(new ScreenGrab.Area(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                frozen.Add(picture);
                pickers.Add(new ColourPickWindow(picture, bounds, screen.Scaling, showHelp: under is null ? pickers.Count == 0 : bounds == under.Bounds));
            }
            if (pickers.Count == 0)
                return;
            // The one under the mouse opens last, so it has the keyboard for Enter and Esc.
            foreach (var window in pickers.OrderBy(p => p.Position == under?.Bounds.Position))
                window.Show();
            var first = await Task.WhenAny(pickers.Select(p => p.Picked));
            var result = await first;
            foreach (var window in pickers)
                window.Dismiss();
            pickers.Clear();
            if (result is not { } colour)
                return;


            if (Clipboard is { } clipboard)
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, colour.Hex);
            services.RememberColour(colour.Hex);
            if (picked is not null)
            {
                picked(colour.Hex);
                return;
            }
            ShowNote("Colour copied", $"{colour.Hex} is on the clipboard. Click to see it in Colours.", () =>
            {
                ShowTool(ToolKind.Colours, services).Tools.ColourInput = colour.Hex;
            });
        }
        finally
        {
            foreach (var window in pickers)
                window.Dismiss();
            foreach (var picture in frozen)
                picture.Dispose();
            foreach (var (window, before) in steppedAside)
                window.WindowState = before;
            // Back where the pick started, e.g. the Colours tool, with the colour in it.
            if (steppedAside.Count > 0 && picked is not null && toolWindows.TryGetValue(ToolKind.Colours, out var tool))
                tool.Activate();
            else if (steppedAside.Count > 0)
                Activate();
            pickingColour = false;
        }
    }

    private static IEnumerable<Window> AppWindows() =>
        Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.Where(w => w is not ColourPickWindow and not RulerWindow)
            : [];
}
