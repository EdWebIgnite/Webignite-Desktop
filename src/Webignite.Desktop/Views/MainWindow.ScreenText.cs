using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Webignite.Core.Tools;
using Webignite.Desktop.Files;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// Text from the screen: a shortcut from any program (Ctrl+Alt+X) or Pick an area in the tool freezes the screen
/// under the mouse, the same picker as a screenshot takes an area, and Windows' own text reading turns it into
/// words, which are copied. Windows only.
/// </summary>
public partial class MainWindow
{
    private bool readingScreen;

    private void WireScreenText(AppServices services)
    {
        services.Go.ReadScreenTextAction = read => ReadScreenText(services, read).Forget("Reading text off the screen", tell: true);
        OnShortcut(Webignite.Core.Tools.GlobalShortcut.ScreenText, () => services.Go.ReadScreenText());
    }

    private async Task ReadScreenText(AppServices services, Action<string>? read)
    {
        if (!OperatingSystem.IsWindows() || readingScreen || services.ReadPictureText is not { } reader)
            return;
        readingScreen = true;
        var steppedAside = new List<(Window Window, WindowState Before)>();
        try
        {
            // Started from inside the app, the app steps aside first, so the words behind it can be picked.
            if (read is not null || AppWindows().Any(w => w.IsActive))
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
            using var picked = ScreenGrabWindow.Crop(frozen, area);
            var text = await reader(Pixels(picked), picked.PixelSize.Width, picked.PixelSize.Height);
            if (string.IsNullOrEmpty(text))
            {
                ShowNote("No text copied", text is null ? ScreenText.NoReader : ScreenText.Nothing, null);
                return;
            }
            if (Clipboard is { } clipboard)
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
            if (read is not null)
                read(text);
            else
                ShowNote("Text copied", ScreenText.Summary(text) + " Click to see it all.", () => ShowTool(ToolKind.ScreenText, services).Tools.ScreenText.ScreenTextRead = text);
        }
        finally
        {
            foreach (var (window, before) in steppedAside)
                window.WindowState = before;
            if (steppedAside.Count > 0 && read is not null && toolWindows.TryGetValue(ToolKind.ScreenText, out var tool))
                tool.Activate();
            else if (steppedAside.Count > 0)
                Activate();
            readingScreen = false;
        }
    }

    // The picture's pixels as plain bytes, blue first, top row first: what Windows' text reading takes.
    private static byte[] Pixels(Bitmap picture)
    {
        var size = picture.PixelSize;
        var pixels = new byte[size.Width * size.Height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            picture.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, size.Width * 4);
        }
        finally
        {
            handle.Free();
        }
        return pixels;
    }
}
