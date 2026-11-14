using System.Runtime.InteropServices;
using Avalonia.Controls;
using Webignite.Data;

namespace Webignite.Desktop;

/// <summary>
/// Windows' own notifications, for when Webignite is behind other windows: the note slides in at the corner of
/// the screen and waits in the notification list, and a click on it brings Webignite forward. It works through
/// an icon by the clock, which is put there with the first notification and taken away when the app closes.
/// Nothing here runs on anything but Windows, and a notification that can't be shown is logged, never thrown.
/// </summary>
public sealed class WindowsNotifier : IDisposable
{
    private const uint Callback = 0x8000 + 0x57;      // WM_APP + a number of our own
    private const int BalloonClicked = 0x0405;        // NIN_BALLOONUSERCLICK
    private const int IconClicked = 0x0202;           // WM_LBUTTONUP on the icon
    private const uint Add = 0, Modify = 1, Delete = 2;
    private const uint WithMessage = 0x01, WithIcon = 0x02, WithTip = 0x04, WithInfo = 0x10;
    private const uint InfoIcon = 0x01, RespectQuietTime = 0x80;

    private readonly TopLevel window;
    private readonly IntPtr handle;
    private readonly Action bringForward;
    private readonly Win32Properties.CustomWndProcHookCallback hook;
    private Action? onClick;
    private bool added;
    private bool disposed;

    /// <param name="bringForward">Brings the main window to the front; runs before a clicked notification's own action.</param>
    public WindowsNotifier(TopLevel window, Action bringForward)
    {
        this.window = window;
        this.bringForward = bringForward;
        handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        hook = Hook;
        if (OperatingSystem.IsWindows() && handle != IntPtr.Zero)
            Win32Properties.AddWndProcHookCallback(window, hook);
    }

    /// <summary>Shows a notification. The action runs if it is clicked; a later notification replaces it.</summary>
    public void Show(string title, string text, Action? clicked)
    {
        if (disposed || !OperatingSystem.IsWindows() || handle == IntPtr.Zero)
            return;
        try
        {
            var data = Data(WithMessage | WithIcon | WithTip | WithInfo);
            data.Info = Fit(text, 255);
            data.InfoTitle = Fit(title, 63);
            data.InfoFlags = InfoIcon | RespectQuietTime;
            if (Shell_NotifyIcon(added ? Modify : Add, ref data))
            {
                added = true;
                onClick = clicked;
            }
            else if (!added && Shell_NotifyIcon(Modify, ref data))
            {
                // The icon was there already (left by a copy that ended badly): use it.
                added = true;
                onClick = clicked;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException or ArgumentException)
        {
            AppLog.Write("Showing a Windows notification", e);
        }
    }

    private static string Fit(string text, int most) => text.Length <= most ? text : text[..(most - 1)] + "…";

    private NotifyIconData Data(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = handle,
        Id = 1,
        Flags = flags,
        CallbackMessage = Callback,
        Icon = OperatingSystem.IsWindows() ? AppIcon() : IntPtr.Zero,
        Tip = "Webignite",
        Info = "",
        InfoTitle = "",
    };

    // The window's own small icon, else the one Windows gives any program.
    private IntPtr AppIcon()
    {
        var icon = SendMessage(handle, 0x007F, 2, IntPtr.Zero);
        if (icon == IntPtr.Zero)
            icon = SendMessage(handle, 0x007F, 0, IntPtr.Zero);
        return icon != IntPtr.Zero ? icon : LoadIcon(IntPtr.Zero, 32512);
    }

    private IntPtr Hook(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != Callback)
            return IntPtr.Zero;
        var what = (int)(lParam.ToInt64() & 0xFFFF);
        if (what is BalloonClicked or IconClicked)
        {
            handled = true;
            var action = what == BalloonClicked ? onClick : null;
            onClick = null;
            bringForward();
            action?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero)
            return;
        Win32Properties.RemoveWndProcHookCallback(window, hook);
        if (!added)
            return;
        try
        {
            var data = Data(0);
            Shell_NotifyIcon(Delete, ref data);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            AppLog.Write("Taking Webignite's icon away from the clock", e);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
}
