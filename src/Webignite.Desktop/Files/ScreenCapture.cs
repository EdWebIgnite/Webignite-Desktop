using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Webignite.Core.Tools;

namespace Webignite.Desktop.Files;

/// <summary>
/// Copies what is on a screen into a picture, and lists the windows on it front to back, so a click can take a
/// whole window. Windows' own drawing calls (GDI); nothing is installed and no browser is involved.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ScreenCapture
{
    /// <summary>The pixels of an area of the desktop, in screen pixels.</summary>
    public static Bitmap Grab(ScreenGrab.Area area)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, area.Width, area.Height);
        var old = SelectObject(memory, bitmap);
        try
        {
            BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, 0x00CC0020 | 0x40000000); // SRCCOPY | CAPTUREBLT
            SelectObject(memory, old);
            var info = new BitmapInfo
            {
                Size = Marshal.SizeOf<BitmapInfo>(),
                Width = area.Width,
                Height = -area.Height, // top row first
                Planes = 1,
                BitCount = 32,
            };
            var picture = new WriteableBitmap(new PixelSize(area.Width, area.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            int lines;
            using (var buffer = picture.Lock())
                lines = GetDIBits(memory, bitmap, 0, (uint)area.Height, buffer.Address, ref info, 0);
            if (lines == 0)
            {
                picture.Dispose();
                throw new IOException("Windows didn't hand over the screen's pixels.");
            }
            return picture;
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            if (ReleaseDC(IntPtr.Zero, screen) == 0)
                Webignite.Data.AppLog.Write("Taking a screenshot", new IOException("Windows didn't take the screen back."));
        }
    }

    /// <summary>The visible windows, front to back, by the edges Windows draws (without the invisible shadow border).</summary>
    public static IReadOnlyList<ScreenGrab.Area> Windows(IntPtr leaveOut)
    {
        var found = new List<ScreenGrab.Area>();
        EnumWindows((window, _) =>
        {
            if (window == leaveOut || !IsWindowVisible(window) || IsIconic(window))
                return true;
            if (DwmGetWindowAttribute(window, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            if (DwmGetWindowAttribute(window, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out Rect rect, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(window, out rect))
                return true;
            if (rect.Right - rect.Left > 1 && rect.Bottom - rect.Top > 1)
                found.Add(new ScreenGrab.Area(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Where the mouse is, in screen pixels.</summary>
    public static (int X, int Y) Cursor() => GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
        public int Colours; // room for one colour entry, unused at 32 bits
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr thing);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr thing);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, IntPtr bits, ref BitmapInfo info, uint usage);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}

/// <summary>
/// A shortcut that works from any program: Windows tells the main window when it is pressed. Registered while the
/// app runs, changed from Settings, and let go when the app closes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlobalHotkey
{
    private const int Id = 0x5747; // "WG"
    private const uint WmHotkey = 0x0312;
    private readonly Avalonia.Controls.TopLevel window;
    private readonly IntPtr handle;
    private readonly Action pressed;
    private readonly Avalonia.Controls.Win32Properties.CustomWndProcHookCallback hook;
    private bool registered;

    public GlobalHotkey(Avalonia.Controls.TopLevel window, Action pressed)
    {
        this.window = window;
        this.pressed = pressed;
        handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        hook = Hook;
        Avalonia.Controls.Win32Properties.AddWndProcHookCallback(window, hook);
    }

    /// <summary>Takes the keys for the shortcut. Returns why not when Windows says another program has them.</summary>
    public string? Set(Hotkey? keys)
    {
        Clear();
        if (keys is null || handle == IntPtr.Zero)
            return null;
        uint modifiers = 0x4000; // MOD_NOREPEAT
        if (keys.Alt) modifiers |= 0x1;
        if (keys.Ctrl) modifiers |= 0x2;
        if (keys.Shift) modifiers |= 0x4;
        if (keys.Win) modifiers |= 0x8;
        registered = RegisterHotKey(handle, Id, modifiers, VirtualKey(keys.Key));
        return registered ? null : $"Another program already uses {keys}. Pick other keys in Settings, Screenshots.";
    }

    private static uint VirtualKey(string key) => key switch
    {
        "PrintScreen" => 0x2C,
        "Pause" => 0x13,
        "ScrollLock" => 0x91,
        "Insert" => 0x2D,
        _ when key.Length == 1 => key[0], // letters and digits are their own codes
        _ => 0x70 + (uint)(int.Parse(key[1..], System.Globalization.CultureInfo.InvariantCulture) - 1), // F1 = 0x70
    };

    private IntPtr Hook(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam == Id)
        {
            handled = true;
            pressed();
        }
        return IntPtr.Zero;
    }

    private void Clear()
    {
        if (registered)
            UnregisterHotKey(handle, Id);
        registered = false;
    }

    /// <summary>Gives the keys back to Windows; called when the main window closes.</summary>
    public void Release()
    {
        Clear();
        Avalonia.Controls.Win32Properties.RemoveWndProcHookCallback(window, hook);
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr window, int id);
}
