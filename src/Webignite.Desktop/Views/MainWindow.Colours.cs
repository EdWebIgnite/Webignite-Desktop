using Avalonia;
using Avalonia.Controls;
using Webignite.Core.Tools;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The colour picker: a shortcut from any program (Ctrl+Alt+C) or Pick from screen in the Colours tool freezes
/// the screen under the mouse, and the colour clicked is copied as its code. Windows only.
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
        try
        {
            var (x, y) = ScreenCapture.Cursor();
            if ((Screens.ScreenFromPoint(new PixelPoint(x, y)) ?? Screens.Primary) is not { } screen)
                return;
            var bounds = screen.Bounds;
            using var frozen = ScreenCapture.Grab(new ScreenGrab.Area(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            var window = new ColourPickWindow(frozen, bounds, screen.Scaling);
            window.Show();
            if (await window.Picked is not { } colour)
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
            pickingColour = false;
        }
    }
}
