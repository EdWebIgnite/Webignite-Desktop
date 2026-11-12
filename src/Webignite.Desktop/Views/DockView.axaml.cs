using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        clock.Tick += (_, _) => Main?.TickClock();
        // As in the sidebar: a page with Ctrl held, or with the middle button, opens in a tab of its own.
        AddHandler(PointerPressedEvent, NewTabPressed, RoutingStrategies.Tunnel);
    }

    private MainViewModel? Main => DataContext as MainViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Main?.TickClock();
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
            items.Add(Item("Take off the dock", () => main.TakeOffDockCommand.Execute(entry), entry.CanTakeOff));
        }
        if (clicked is BarItem bar)
        {
            items.Add(Item("Move to the other side", () => main.MoveBarAcrossCommand.Execute(bar)));
            items.Add(Item("Take it off", () => main.TakeOffBarCommand.Execute(bar)));
        }
        if (items.Count > 0)
            items.Add(new Separator());
        items.Add(Item("Choose what is on the menu…", () => main.EditMenuCommand.Execute(null)));
        items.Add(Item("Choose what is beside the dock…", () => main.ChooseBarCommand.Execute(null)));
        new ContextMenu { ItemsSource = items }.Open(this);
        e.Handled = true;
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
            Main?.PickFromMenuCommand.Execute(entry);
            return;
        }
        // A group's short list is made when it is asked for and let go when it closes.
        var list = new StackPanel { MinWidth = 230, Margin = new Thickness(-4) };
        var heading = new TextBlock { Text = entry.Title, Margin = new Thickness(10, 2, 0, 6) };
        heading.Classes.Add("label");
        heading.Classes.Add("caps");
        list.Children.Add(heading);
        var flyout = new Flyout { Placement = PlacementMode.Top, VerticalOffset = -10, Content = list };
        foreach (var inside in entry.Inside)
        {
            var icon = new Controls.Icon { Size = 17, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            icon.Bind(Controls.Icon.DataProperty, new Avalonia.Data.Binding { Source = inside.Icon, Converter = Controls.IconConverter.Instance });
            icon.Bind(Controls.Icon.ForegroundProperty, this.GetResourceObservable("Muted"));
            var row = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 11,
                    Children = { icon, new TextBlock { Text = inside.Title, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } },
                },
            };
            row.Classes.Add("groupentry");
            if (inside.IsCurrent)
                row.Classes.Add("current");
            row.Click += (_, _) =>
            {
                flyout.Hide();
                Main?.PickFromMenuCommand.Execute(inside);
            };
            list.Children.Add(row);
        }
        flyout.ShowAt(button);
    }
}
