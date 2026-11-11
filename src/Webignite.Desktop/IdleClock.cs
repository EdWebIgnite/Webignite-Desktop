using System.Runtime.InteropServices;

namespace Webignite.Desktop;

/// <summary>
/// When the person last used the PC (any key or click, in any program), for the chat's automatic Away. Windows says
/// how long since the last input; elsewhere it's taken as now.
/// </summary>
public static class IdleClock
{
    public static DateTimeOffset LastActive()
    {
        if (!OperatingSystem.IsWindows())
            return DateTimeOffset.UtcNow;
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            return DateTimeOffset.UtcNow;
        // Both counts wrap after 49 days; unsigned subtraction gives the right gap anyway.
        var idle = unchecked((uint)Environment.TickCount - info.Time);
        return DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(idle);
    }

    /// <summary>The short sound Windows plays for a notification, for a new chat message.</summary>
    public static void Chime()
    {
        if (OperatingSystem.IsWindows())
            PlaySound("Notification.Default", IntPtr.Zero, SoundAlias | SoundAsync | SoundNoDefault);
    }

    private const uint SoundAsync = 0x1;
    private const uint SoundNoDefault = 0x2;
    private const uint SoundAlias = 0x10000;

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);
}
