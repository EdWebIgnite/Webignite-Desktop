using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia;
using Webignite.Data;
using Webignite.Desktop.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly WindowNotificationManager notes;

    public MainWindow()
    {
        InitializeComponent();
        // Before the ListBox sees the press, so clicking a heading folds its group instead of selecting it.
        Nav.AddHandler(PointerPressedEvent, NavPressed, RoutingStrategies.Tunnel);
        PlannerNav.AddHandler(PointerPressedEvent, NavPressed, RoutingStrategies.Tunnel);
        DragNav(Nav, planner: false);
        DragNav(PlannerNav, planner: true);
        // A task dragged out of a project's list can be dropped on another project here.
        DropZones.Add(new DropZone(PlannerNav, MainViewModel.TakesTask, (item, target) => (DataContext as MainViewModel)?.DropTask(item, target)));
        // Made before the window's template is applied, so it can put itself in the window's top layer.
        notes = new WindowNotificationManager(this) { Position = NotificationPosition.BottomRight, MaxItems = 3 };
    }

    private MainViewModel? wired; // the view model whose events this window listens to

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainViewModel main || ReferenceEquals(main, wired))
            return;
        // Only the first time: the app has one window and one main view model, so listening again would only
        // add a second copy of each handler.
        if (wired is not null)
            return;
        wired = main;
        WirePopOuts(main);
        WireSplit(main);
        main.WindowIsActive = () => IsActive && WindowState != WindowState.Minimized;
        main.FlashTaskbar = Flash;
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ShowingShortcuts))
                ShowShortcutList(main);
        };
        main.Search.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(QuickSearchViewModel.IsOpen) && main.Search.IsOpen)
                Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Input);
        };
        if (App.Services is { } services)
        {
            services.ShowNote = ShowNote;
            services.IsInFront = () => IsActive && WindowState != WindowState.Minimized;
            if (OperatingSystem.IsWindows())
            {
                // Made with the first notification, when the window is sure to be up; taken away again as the window closes.
                services.ShowWindowsNote = (title, text, onClick) => (windowsNotes ??= new WindowsNotifier(this, BringForward)).Show(title, text, onClick);
                Closed += (_, _) =>
                {
                    services.ShowWindowsNote = null;
                    windowsNotes?.Dispose();
                    windowsNotes = null;
                };
            }
            Restore(services.Settings.Window, main);
            ApplyAppearance(services.Settings.Appearance);
            services.Settings.AppearanceChanged += ApplyAppearance;
            FollowMenu(main);
            WireScreenshots(main, services);
            WireRecording(main, services);
            WireQuickNote(services);
            WireTools(services);
            WireColourPicker(services);
            WireRuler(services);
            WireScreenText(services);
            WireShortcuts(services);
            WirePictures(services);
            WireChat(services, main);
        }
    }

    private async Task CloseWhenReady(MainViewModel main, AppServices services)
    {
        preparing = true;
        try
        {
            await FinishRecordingBeforeClose();
            string? problem;
            try
            {
                problem = await main.PrepareToCloseAsync();
            }
            catch (Exception e)
            {
                AppLog.Write("Saving open work before closing failed", e);
                problem = "Open work couldn't all be saved: " + e.Message;
            }
            if (problem is not null && !warned)
            {
                // Said once. Closing again leaves anyway, so a mail server that's down can't keep the app open.
                warned = true;
                services.ShowNote?.Invoke("Not closed yet", problem + " Close again to leave without it.", null);
                return;
            }
        }
        finally
        {
            preparing = false;
        }
        readyToClose = true;
        Close();
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
        // The tabs come back as they were; before there were tabs, the page that was open.
        if (App.Services?.Settings.Tabs is { Pages.Count: > 0 } tabs)
        {
            main.RestoreTabs(tabs);
            Dispatcher.UIThread.Post(() => RestorePopOuts(main, tabs));
        }
        else if (Enum.TryParse<Page>(placement.LastPage, out var key) && main.Modules.FirstOrDefault(m => m.Key == key) is { } page)
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

    private bool readyToClose, preparing, warned;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || App.Services is not { } services || DataContext is not MainViewModel main)
            return;
        // Closing waits until open work is saved and a mail in its undo wait has gone. Shutting Windows down
        // can't wait, so it skips this.
        if (!readyToClose && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            if (!preparing)
                CloseWhenReady(main, services).Forget("Getting ready to close");
            return;
        }
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
        services.Settings.Tabs = main.TabState(Placement);
        ClosePopOutsQuietly();
    }

    /// <summary>Motion: the window's class switches the eased styles in Styles/Motion.axaml, and pages get their fade.</summary>
    private void ApplyAppearance(AppearanceSettings appearance)
    {
        Palettes.Apply(appearance);
        Looks.Follow(this);
        Looks.Apply(appearance);
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
        var entry = (sender as ListBox)?.Items[e.Index];
        var heading = entry is NavGroup or NavHeading;
        item.Classes.Set("heading", heading);
        item.Classes.Set("gap", entry is NavGroup { CanFold: false } or NavHeading { IsGap: true });
        item.Classes.Set("back", entry is SidebarBack);
        item.Focusable = !heading;
    }

    // A heading folds its group. A page with Ctrl held, or with the middle button, opens in a new tab.
    private void NavPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Properties;
        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext;
        if (item is Module module && DataContext is MainViewModel main
            && (point.IsMiddleButtonPressed || (point.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control))))
        {
            main.OpenInNewTab(module);
            e.Handled = true;
            return;
        }
    }

    // Every entry can be dragged to a new place; a click (a press without a drag) picks it, or folds a group.
    // In Planner's sidebar, Home and Projects stay where they are; the pages under Plan and the projects move.
    private void DragNav(ListBox list, bool planner)
    {
        bool Fixed(object item) => planner && item is Module { Key: Page.Home or Page.Projects };
        DragReorder.Attach(list, new DragRules
        {
            CanDrag = item => DataContext is MainViewModel main && !Fixed(item) && (main.Sidebar.CanDrag(item) || item is ProjectLink { Kind: ProjectLinkKind.Project }),
            CanDrop = (item, target, below) => DataContext is MainViewModel main && !Fixed(target)
                && (item is ProjectLink ? main.CanMoveProject(item, target, below) : main.Sidebar.CanMoveTo(item, target, below)),
            Drop = (item, target, below) =>
            {
                if (DataContext is not MainViewModel main)
                    return;
                if (item is ProjectLink)
                    main.MoveProject(item, target, below);
                else
                    main.Sidebar.MoveTo(item, target, below);
            },
            Label = item => item switch
            {
                Module m => m.Title,
                ProjectLink l => l.Title,
                NavGroup g => g.Title,
                ToolLink t => t.Title,
                SectionLink section => section.Title,
                _ => "",
            },
            Click = item =>
            {
                if (item is NavGroup group)
                    group.Toggle();
                // The entry already picked, clicked again (Planner after its back arrow, Projects while a project is
                // open): the list sees no change, so it's picked again here.
                else if (Equals(list.SelectedItem, item) && DataContext is MainViewModel main)
                    main.SelectedNav = item;
                else
                    list.SelectedItem = item;
            },
        });
    }

    // Picking a tab shows it. While a tab's page is being swapped the strip briefly has nothing picked; that is ignored.
    private void TabPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (TabStrip.SelectedItem is PageHost tab && DataContext is MainViewModel main && main.Current != tab)
            main.Current = tab;
    }

    // A middle click on a tab closes it, as in a browser.
    private void TabReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Middle && (sender as Control)?.DataContext is PageHost tab && DataContext is MainViewModel main)
        {
            main.CloseTab(tab);
            e.Handled = true;
        }
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

    // A click on the dimmed part closes the list; one inside the box is for the box.
    private void CloseShortcuts(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender) && DataContext is MainViewModel main)
            main.ShowingShortcuts = false;
    }

    // The list is built when F1 first asks for it, and let go when it closes, so it costs nothing at start-up.
    private void ShowShortcutList(MainViewModel main)
    {
        if (!main.ShowingShortcuts)
        {
            ShortcutHost.Content = null;
            return;
        }
        var list = new ShortcutListView { DataContext = main.ShortcutsPart };
        ShortcutHost.Content = list;
        Avalonia.Threading.Dispatcher.UIThread.Post(list.FocusSearch, Avalonia.Threading.DispatcherPriority.Background);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainViewModel { MenuPart.IsMenuOpen: true } menu)
        {
            menu.MenuPart.IsMenuOpen = false;
            e.Handled = true;
            return;
        }
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
        // Shortcuts that writing uses as well: bold and a link come first. The window's key bindings take a key
        // before the page sees it, so these two are taken here instead, only once nothing on the page wanted them.
        if (!e.Handled && e.KeyModifiers == KeyModifiers.Control && DataContext is MainViewModel keys)
        {
            if (e.Key == Key.B)
            {
                keys.Sidebar.ToggleCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.K)
            {
                keys.Search.ShowCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }
        base.OnKeyDown(e);
    }

    private void OpenSearchHit(object? sender, RoutedEventArgs e) => Search?.OpenChosen();

    private void CloseSearch(object? sender, PointerPressedEventArgs e) => Search?.Hide();

    // Clicks inside the box shouldn't reach the dimmed background and close it.
    private void KeepSearchOpen(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    /// <summary>A note in the bottom corner of the window. It fades after a few seconds; clicking it runs the action.</summary>
    private static readonly TimeSpan NoteShows = TimeSpan.FromSeconds(8);

    // What was said in the corner lately, so the same note raised twice in a row shows once.
    private readonly Dictionary<string, DateTime> lately = [];

    private WindowsNotifier? windowsNotes;

    // From a click on a Windows notification: the window comes back from the taskbar and to the front.
    private void BringForward()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void ShowNote(string title, string text, Action? onClick)
    {
        var now = DateTime.UtcNow;
        foreach (var gone in lately.Where(n => now - n.Value > NoteShows).Select(n => n.Key).ToList())
            lately.Remove(gone);
        // Each undo is its own, so those always show.
        var key = title + "\n" + text;
        if (onClick is null || text != "Click here to undo.")
        {
            if (lately.ContainsKey(key))
                return;
            lately[key] = now;
        }
        notes.Show(new Notification(title, text, NotificationType.Information, NoteShows, onClick: () =>
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

    private FlyoutBase? groupList;

    // A group's icon on the folded sidebar: its pages open in a list beside it. Only one list is open at a time.
    private void ShowGroupList(object? sender, Avalonia.Input.PointerEventArgs e) => ShowGroupList(sender);

    private void ShowGroupList(object? sender, Avalonia.Input.TappedEventArgs e) => ShowGroupList(sender);

    private void ShowGroupList(object? sender)
    {
        if (sender is not Control icon || FlyoutBase.GetAttachedFlyout(icon) is not { } list || list.IsOpen)
            return;
        if (groupList != list)
            groupList?.Hide();
        groupList = list;
        list.ShowAt(icon);
    }

    // A page, tool or link picked in a group's list opens as if picked in the sidebar.
    private void OpenGroupEntry(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: GroupEntry entry } || DataContext is not MainViewModel main)
            return;
        groupList?.Hide();
        main.SelectedNav = entry.Target;
    }
}
