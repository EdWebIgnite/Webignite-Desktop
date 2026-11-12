using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The dock: the pages, tools and groups used most in a bar along the bottom, with the rest behind More, and
/// beside it the small things the person picked (the day, a clock, the timer, chat). A group opens a short list
/// of what is in it just above the dock.
/// </summary>
public partial class DockView : UserControl
{
    // The clock is looked at a few times a minute while the dock is on screen, and not at all once it is gone.
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(10) };

    public DockView()
    {
        InitializeComponent();
        Looks.Attach(this, "Menu");
        clock.Tick += (_, _) => Main?.DockBarPart.TickClock();
        // As in the sidebar: a page with Ctrl held, or with the middle button, opens in a tab of its own.
        AddHandler(PointerPressedEvent, NewTabPressed, RoutingStrategies.Tunnel);
        // Anything on the dock can be dragged along it to another place.
        Controls.DragReorder.Attach(DockItems, new Controls.DragRules
        {
            CanDrag = item => item is MenuEntry { PageKey: not null },
            CanDrop = (item, target, _) => item is MenuEntry && target is MenuEntry { PageKey: not null } && !ReferenceEquals(item, target),
            Drop = (item, target, after) =>
            {
                if (item is MenuEntry entry && target is MenuEntry beside)
                    Main?.MenuPart.MoveOnDock(entry, beside, after);
            },
            Across = _ => true,
            Label = item => (item as MenuEntry)?.Title ?? "",
        });
    }

    private MainViewModel? Main => DataContext as MainViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Main?.DockBarPart.TickClock();
        clock.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        clock.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    // One right-click menu for the dock and what is beside it, made when it is asked for: what it offers depends
    // on what was clicked (a page on the dock, one of the things beside it, or the bar itself).
    private void Asked(object? sender, ContextRequestedEventArgs e)
    {
        if (Main is not { } main)
            return;
        var clicked = (e.Source as StyledElement)?.DataContext;
        var items = new List<object>();
        if (clicked is MenuEntry entry)
        {
            // A page can be opened beside what is on screen, or in a window of its own to put on another screen.
            if (entry.Item is Module page)
            {
                items.Add(Item("Open in a new tab", () => main.OpenInNewTab(page)));
                items.Add(Item("Open in a new window", () => main.OpenInWindow(page)));
                items.Add(new Separator());
            }
            items.Add(Item("Take off the dock", () => main.MenuPart.TakeOffDockCommand.Execute(entry), entry.CanTakeOff));
        }
        if (clicked is BarItem bar)
        {
            items.Add(Item("Move to the other side", () => main.DockBarPart.MoveBarAcrossCommand.Execute(bar)));
            items.Add(Item("Take it off", () => main.DockBarPart.TakeOffBarCommand.Execute(bar)));
        }
        if (items.Count > 0)
            items.Add(new Separator());
        items.Add(Item("Choose what is on the menu…", () => main.MenuPart.EditMenuCommand.Execute(null)));
        items.Add(Item("Choose what is beside the dock…", () => main.DockBarPart.ChooseBarCommand.Execute(null)));
        new ContextMenu { ItemsSource = items }.Open(this);
        e.Handled = true;
    }

    // A group's list is made when it is asked for and let go when it closes: its name, then what is in it, each a
    // row to click. Rows can be dragged to another place in the group, which is the order the menu keeps everywhere.
    private void ShowGroup(Button button, MenuEntry group)
    {
        var flyout = new Flyout { Placement = PlacementMode.Top, VerticalOffset = -12 };
        flyout.FlyoutPresenterClasses.Add("dockpop");
        if (GroupList(group, flyout.Hide) is not { } list)
            return;
        flyout.Content = list;
        flyout.ShowAt(button);
    }

    /// <summary>
    /// What a group's pop-up holds, as a card of its own. For the screenshot tool, which can't draw pop-ups.
    /// </summary>
    public Control? GroupPicture(MenuEntry group)
    {
        var card = new Border { Child = GroupList(group, () => { }) };
        card.Classes.Add("dockpopcard");
        return card;
    }

    private StackPanel? GroupList(MenuEntry group, Action picked)
    {
        if (Main is not { } main || group.PageKey is not { } key)
            return null;
        var rows = new ItemsControl { ItemTemplate = new FuncDataTemplate<MenuEntry>((entry, _) => Row(entry), supportsRecycling: false) };
        var title = new TextBlock { Text = group.Title, Margin = new Thickness(10, 4, 10, 8) };
        title.Classes.Add("grouptitle");
        void Fill() => rows.ItemsSource = main.MenuPart.OnDock(key)?.Inside ?? [];
        Fill();
        rows.AddHandler(Button.ClickEvent, (_, e) =>
        {
            if ((e.Source as StyledElement)?.DataContext is not MenuEntry { IsSection: false } entry)
                return;
            picked();
            main.MenuPart.PickFromMenuCommand.Execute(entry);
        });
        Controls.DragReorder.Attach(rows, new Controls.DragRules
        {
            CanDrag = item => item is MenuEntry { IsSection: false, Item: Module or ToolLink },
            CanDrop = (item, target, _) => item is MenuEntry entry && target is MenuEntry beside && main.MenuPart.CanMoveInGroup(entry, beside),
            Drop = (item, target, below) =>
            {
                if (item is MenuEntry entry && target is MenuEntry beside)
                    main.MenuPart.MoveInGroup(entry, beside, below);
                Fill();
            },
            Label = item => (item as MenuEntry)?.Title ?? "",
        });
        return new StackPanel { MinWidth = 264, Children = { title, rows } };
    }

    private Control Row(MenuEntry? entry)
    {
        if (entry is null)
            return new Border();
        if (entry.IsSection)
        {
            var label = new TextBlock { Text = entry.Title, Margin = new Thickness(10, 10, 10, 4) };
            label.Classes.Add("label");
            label.Classes.Add("caps");
            return label;
        }
        Control mark;
        if (entry.Icon.Length > 0)
        {
            var icon = new Controls.Icon { Size = 16, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            icon.Bind(Controls.Icon.DataProperty, new Avalonia.Data.Binding { Source = entry.Icon, Converter = Controls.IconConverter.Instance });
            mark = new Border { Child = icon };
            mark.Classes.Add("grouptile");
        }
        else
        {
            // A project with no picture of its own: a dot, as the sidebar gives it.
            mark = new Border { Child = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6 } };
            mark.Classes.Add("groupdot");
        }
        var words = new TextBlock { Text = entry.Title, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        var line = new DockPanel { Children = { mark } };
        DockPanel.SetDock(mark, Dock.Left);
        // A page's live count, such as unread mail, at the end of its row.
        if (entry.Module is { HasBadge: true } counted)
        {
            var count = new TextBlock { Text = counted.Badge.ToString(System.Globalization.CultureInfo.CurrentCulture), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(10, 0, 2, 0) };
            count.Classes.Add("muted");
            DockPanel.SetDock(count, Dock.Right);
            line.Children.Add(count);
        }
        line.Children.Add(words);
        var row = new Button { Content = line };
        row.Classes.Add("groupentry");
        row.Classes.Set("current", entry.IsCurrent);
        // A project sits in under Projects; a link with a picture of its own (My tasks, Templates) reads as a page.
        row.Classes.Set("sub", entry.Item is SubLink { HasIcon: false });
        return row;
    }

    private void NewTabPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Properties;
        if ((e.Source as StyledElement)?.DataContext is MenuEntry { Item: Module page } && Main is { } main
            && (point.IsMiddleButtonPressed || (point.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control))))
        {
            main.OpenInNewTab(page);
            e.Handled = true;
        }
    }

    private static MenuItem Item(string header, Action picked, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => picked();
        return item;
    }

    private void DockClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MenuEntry entry } button)
            return;
        if (!entry.IsGroup)
        {
            Main?.MenuPart.PickFromMenuCommand.Execute(entry);
            return;
        }
        ShowGroup(button, entry);
    }
}
