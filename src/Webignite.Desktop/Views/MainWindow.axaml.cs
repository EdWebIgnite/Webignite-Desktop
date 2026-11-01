using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        main.Search.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(QuickSearchViewModel.IsOpen) && main.Search.IsOpen)
                Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Input);
        };
        if (App.Services is { } services)
        {
            services.ShowNote = ShowNote;
            ApplyAppearance(services.Appearance);
            services.AppearanceChanged += ApplyAppearance;
        }
    }

    /// <summary>Motion: the window's class switches the eased styles in Styles/Motion.axaml, and pages get their fade.</summary>
    private void ApplyAppearance(AppearanceSettings appearance)
    {
        Classes.Set("motion-full", appearance.Motion == Motion.Full);
        Classes.Set("motion-reduced", appearance.Motion == Motion.Reduced);
        ScrollingHost.PageTransition = FadeRise.For(appearance.Motion);
        FullHost.PageTransition = FadeRise.For(appearance.Motion);
    }

    // Group headings in the sidebar are only labels: they can't be picked, hovered or reached with the arrow keys.
    private void PrepareNavItem(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not ListBoxItem item || DataContext is not MainViewModel main)
            return;
        var heading = main.NavItems[e.Index] is NavHeading;
        item.Classes.Set("heading", heading);
        item.Classes.Set("gap", main.NavItems[e.Index] is NavHeading { Title.Length: 0 });
        item.IsEnabled = !heading;
        item.Focusable = !heading;
    }

    private QuickSearchViewModel? Search => (DataContext as MainViewModel)?.Search;

    private void SearchKeys(object? sender, KeyEventArgs e)
    {
        if (Search is not { } search)
            return;
        switch (e.Key)
        {
            case Key.Down: search.Move(1); e.Handled = true; break;
            case Key.Up: search.Move(-1); e.Handled = true; break;
            case Key.Enter: search.OpenChosen(); e.Handled = true; break;
            case Key.Escape: search.Hide(); e.Handled = true; break;
        }
    }

    private void OpenSearchHit(object? sender, RoutedEventArgs e) => Search?.OpenChosen();

    private void CloseSearch(object? sender, PointerPressedEventArgs e) => Search?.Hide();

    // Clicks inside the box shouldn't reach the dimmed background and close it.
    private void KeepSearchOpen(object? sender, PointerPressedEventArgs e) => e.Handled = true;

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
