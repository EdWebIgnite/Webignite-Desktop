using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly WindowNotificationManager notes;

    public MainWindow()
    {
        InitializeComponent();
        // Made before the window's template is applied, so it can put itself in the window's top layer.
        notes = new WindowNotificationManager(this) { Position = NotificationPosition.BottomRight, MaxItems = 3 };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainViewModel main)
            return;
        main.WindowIsActive = () => IsActive && WindowState != WindowState.Minimized;
        main.FlashTaskbar = Flash;
        if (App.Services is { } services)
            services.ShowNote = ShowNote;
    }

    /// <summary>A note in the bottom corner of the window. It fades after a few seconds; clicking it runs the action.</summary>
    private void ShowNote(string title, string text, Action? onClick)
    {
        notes.Show(new Notification(title, text, NotificationType.Information, TimeSpan.FromSeconds(8), onClick: () =>
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            onClick?.Invoke();
        }));
    }

    // Windows: the taskbar button flashes until the window is brought to the front.
    private void Flash()
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle()?.Handle is not { } handle)
            return;
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = handle,
            Flags = FlashTray | FlashUntilForeground,
            Count = 3,
        };
        FlashWindowEx(ref info);
    }

    private const uint FlashTray = 0x2;
    private const uint FlashUntilForeground = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
