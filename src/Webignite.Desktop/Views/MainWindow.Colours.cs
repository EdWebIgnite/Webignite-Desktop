using Avalonia;
using Avalonia.Controls;
using Webignite.Core.Tools;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The colour picker: a shortcut from any program (Ctrl+Alt+C), or picking from the screen anywhere in the app,
/// freezes every monitor as it is, the app included, and the colour clicked is copied as its code. Windows only.
/// </summary>
public partial class MainWindow
{
    private bool pickingColour;

    private void WireColourPicker(AppServices services)
    {
        services.Go.PickColourAction = picked => PickColour(services, picked).Forget("Picking a colour", tell: true);
        OnShortcut(Webignite.Core.Tools.GlobalShortcut.ColourPicker, () => services.Go.PickColour());
    }

    private async Task PickColour(AppServices services, Action<string>? picked)
    {
        if (!OperatingSystem.IsWindows() || pickingColour)
            return;
        pickingColour = true;
        var pickers = new List<ColourPickWindow>();
        var frozen = new List<Avalonia.Media.Imaging.Bitmap>();
        // Where the pick was started from, to go back to once it is done.
        var startedIn = AppWindows().FirstOrDefault(w => w.IsActive);
        try
        {
            // The app stays where it is: the colour wanted is often in it, on a page in the Browser or a client's
            // logo in Files. Started from inside the app, the menu or pop-up that was clicked is given a moment to
            // close, so it isn't frozen into the picture.
            if (picked is not null || startedIn is not null)
                await Task.Delay(180);

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
                ShowTool(ToolKind.Colours, services).Tools.Colours.ColourInput = colour.Hex;
            });
        }
        finally
        {
            foreach (var window in pickers)
                window.Dismiss();
            foreach (var picture in frozen)
                picture.Dispose();
            // Back where the pick started, e.g. the Colours tool, with the colour in it.
            if (startedIn is { IsVisible: true })
                startedIn.Activate();
            pickingColour = false;
        }
    }

    private static IEnumerable<Window> AppWindows() =>
        Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.Where(w => w is not ColourPickWindow and not RulerWindow)
            : [];
}
