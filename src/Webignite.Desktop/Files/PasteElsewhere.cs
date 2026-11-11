using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Webignite.Desktop.Files;

/// <summary>
/// Pasting into another program: remember which window the person was typing in, and later bring it forward
/// and press Ctrl+V there. What is pasted is whatever is on the clipboard.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PasteElsewhere
{
    /// <summary>The window in front right now, unless it is one of Webignite's own. Zero when there is none to go back to.</summary>
    public static IntPtr WindowInFront()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            return IntPtr.Zero;
        _ = GetWindowThreadProcessId(window, out var process);
        return process == (uint)Environment.ProcessId ? IntPtr.Zero : window;
    }

    /// <summary>Brings that window forward and presses Ctrl+V in it. False when the window has gone or won't come forward.</summary>
    public static async Task<bool> PasteAsync(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window) || !SetForegroundWindow(window))
            return false;
        // The other program needs a moment to take the keyboard back before the keys arrive.
        await Task.Delay(120);
        if (GetForegroundWindow() != window)
            return false;
        const byte control = 0x11, v = 0x56;
        const uint up = 0x0002;
        keybd_event(control, 0, 0, UIntPtr.Zero);
        keybd_event(v, 0, 0, UIntPtr.Zero);
        keybd_event(v, 0, up, UIntPtr.Zero);
        keybd_event(control, 0, up, UIntPtr.Zero);
        return true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll")]
#pragma warning disable IDE1006 // Windows' own name for it.
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
#pragma warning restore IDE1006
}
