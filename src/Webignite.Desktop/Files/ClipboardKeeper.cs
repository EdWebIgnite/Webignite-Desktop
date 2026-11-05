using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Webignite.Core.Tools;

namespace Webignite.Desktop.Files;

/// <summary>
/// Clipboard history: the last <see cref="ClipboardHistory.Most"/> things copied in any program (text and pictures), kept in
/// memory while Webignite is open and gone when it closes. Windows tells the main window when the clipboard
/// changes (no polling). A copy marked private (Webignite's own password copies, password managers) is skipped.
/// </summary>
public sealed class ClipboardKeeper
{
    private const uint WmClipboardUpdate = 0x031D;

    // Windows' convention for "don't keep this": password managers and Webignite's site logins set it.
    private static readonly string[] PrivateFormats = ["ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory"];

    private IReadOnlyList<Clip> items = [];
    private TopLevel? window;
    private Win32Properties.CustomWndProcHookCallback? hook;
    private IntPtr handle;

    public IReadOnlyList<Clip> Items => items;

    /// <summary>Raised on the screen's thread when something is added or taken off.</summary>
    public event Action? Changed;

    public void Add(string? text) => Add(new Clip(text));

    public void Add(Clip clip)
    {
        var added = ClipboardHistory.Add(items, clip);
        if (ReferenceEquals(added, items))
            return;
        items = added;
        Changed?.Invoke();
    }

    public void Remove(Clip clip)
    {
        items = ClipboardHistory.Remove(items, clip.Key);
        Changed?.Invoke();
    }

    public void Clear()
    {
        items = [];
        Changed?.Invoke();
    }

    /// <summary>Starts listening on the main window. Windows only; elsewhere the history fills only from Add.</summary>
    public void Watch(TopLevel main)
    {
        if (!OperatingSystem.IsWindows() || window is not null)
            return;
        handle = main.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || !AddClipboardFormatListener(handle))
            return;
        window = main;
        hook = Hook;
        Win32Properties.AddWndProcHookCallback(main, hook);
    }

    /// <summary>Stops listening and forgets everything; called when the main window closes.</summary>
    public void Stop()
    {
        if (OperatingSystem.IsWindows() && window is not null && hook is not null)
        {
            RemoveClipboardFormatListener(handle);
            Win32Properties.RemoveWndProcHookCallback(window, hook);
        }
        window = null;
        hook = null;
        items = [];
    }

    private IntPtr Hook(IntPtr _, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmClipboardUpdate && window?.Clipboard is { } clipboard)
            Dispatcher.UIThread.Post(() => Read(clipboard).Forget("Reading the clipboard for its history"));
        return IntPtr.Zero;
    }

    private async Task Read(IClipboard clipboard)
    {
        var formats = await clipboard.GetDataFormatsAsync();
        if (formats.Any(f => PrivateFormats.Contains(f.Identifier, StringComparer.OrdinalIgnoreCase)))
            return;
        if (formats.Contains(DataFormat.Text))
        {
            Add(await clipboard.TryGetTextAsync());
        }
        else if (formats.Contains(DataFormat.Bitmap) && await clipboard.TryGetBitmapAsync() is { } bitmap)
        {
            // Kept as PNG, made off the screen's thread; the picture itself is let go straight after.
            using (bitmap)
            {
                var size = bitmap.PixelSize;
                var png = await Task.Run(() =>
                {
                    using var stream = new MemoryStream();
                    bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    return stream.ToArray();
                });
                Add(new Clip(null, png, size.Width, size.Height));
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr window);
}
