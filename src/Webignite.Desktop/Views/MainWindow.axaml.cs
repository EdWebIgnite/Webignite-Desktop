using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly WindowNotificationManager notes;

    public MainWindow()
    {
        InitializeComponent();
        // Before the ListBox sees the press, so clicking a heading folds its group instead of selecting it.
        Nav.AddHandler(PointerPressedEvent, FoldGroup, RoutingStrategies.Tunnel);
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
            Restore(services.Settings.Window, main);
            ApplyAppearance(services.Settings.Appearance);
            services.Settings.AppearanceChanged += ApplyAppearance;
        }
    }

    /// <summary>Opens the window where it was last time, at the same size and on the same page.</summary>
    private void Restore(WindowPlacement? placement, MainViewModel main)
    {
        if (placement is null)
            return;
        Width = Math.Max(MinWidth, placement.Width);
        Height = Math.Max(MinHeight, placement.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new Avalonia.PixelPoint(placement.X, placement.Y);
        if (placement.Maximized)
            WindowState = WindowState.Maximized;
        if (Enum.TryParse<Page>(placement.LastPage, out var key) && main.Modules.FirstOrDefault(m => m.Key == key) is { } page)
            main.Selected = page;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // A screen that was there last time may be unplugged now: bring the window back into view.
        if (Screens.ScreenFromPoint(Position + new Avalonia.PixelPoint(40, 20)) is null && Screens.Primary is { } screen)
            Position = new Avalonia.PixelPoint(
                screen.WorkingArea.X + Math.Max(0, (screen.WorkingArea.Width - (int)(Width * screen.Scaling)) / 2),
                screen.WorkingArea.Y + Math.Max(0, (screen.WorkingArea.Height - (int)(Height * screen.Scaling)) / 2));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || App.Services is not { } services || DataContext is not MainViewModel main)
            return;
        var before = services.Settings.Window;
        var normal = WindowState == WindowState.Normal;
        services.Settings.Window = new WindowPlacement
        {
            // Maximised or minimised: keep the normal size from last time so un-maximising still looks right.
            X = normal ? Position.X : before?.X ?? Position.X,
            Y = normal ? Position.Y : before?.Y ?? Position.Y,
            Width = normal ? Width : before?.Width ?? Width,
            Height = normal ? Height : before?.Height ?? Height,
            Maximized = WindowState == WindowState.Maximized,
            LastPage = main.Selected.Key.ToString(),
        };
    }

    /// <summary>Motion: the window's class switches the eased styles in Styles/Motion.axaml, and pages get their fade.</summary>
    private void ApplyAppearance(AppearanceSettings appearance)
    {
        Palettes.Apply(appearance);
        Classes.Set("motion-full", appearance.Motion == Motion.Full);
        Classes.Set("motion-reduced", appearance.Motion == Motion.Reduced);
        Controls.Spinner.Still = appearance.Motion == Motion.Off;
        ScrollingHost.PageTransition = FadeRise.For(appearance.Motion);
        FullHost.PageTransition = FadeRise.For(appearance.Motion);
    }

    // Group headings in the sidebar can't be picked or reached with the arrow keys; a click folds the group.
    private void PrepareNavItem(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not ListBoxItem item || DataContext is not MainViewModel main)
            return;
        var heading = main.Sidebar.Items[e.Index] is NavGroup;
        item.Classes.Set("heading", heading);
        item.Classes.Set("gap", main.Sidebar.Items[e.Index] is NavGroup { CanFold: false });
        item.Focusable = !heading;
    }

    private void FoldGroup(object? sender, PointerPressedEventArgs e)
    {
        // A right click opens the menu instead.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not NavGroup group)
            return;
        group.Toggle();
        e.Handled = true;
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

    private void CloseShortcuts(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel main)
            main.ShowingShortcuts = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainViewModel { ShowingShortcuts: true } main)
        {
            main.ShowingShortcuts = false;
            e.Handled = true;
            return;
        }
        // Esc nobody else wanted: close what is open on the page, e.g. an invoice or a client.
        if (e.Key == Key.Escape && !e.Handled && DataContext is MainViewModel page && page.GoBack())
        {
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
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
