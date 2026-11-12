using Avalonia.Controls;
using Avalonia.Input;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>The whole menu, opened over the page by the Menu button, the dock's More or Ctrl+M.</summary>
public partial class MenuOverlayView : UserControl
{
    public MenuOverlayView()
    {
        InitializeComponent();
        Looks.Attach(this, "Menu");
        // While the menu is being edited, an entry can be dragged beside another to change the order; one dragged
        // out of Available comes back on where it is dropped. A plain click still takes off or puts back.
        var rules = new Controls.DragRules
        {
            CanDrag = item => item is MenuEntry { Editing: true } entry && DataContext is MainViewModel main && main.Sidebar.CanDrag(entry.Item) && entry.Item is not Module { InPlanner: true },
            CanDrop = (item, target, below) => item is MenuEntry entry && target is MenuEntry onto && DataContext is MainViewModel main && main.MenuPart.CanMoveInMenu(entry, onto),
            Drop = (item, target, below) =>
            {
                if (item is MenuEntry entry && target is MenuEntry onto && DataContext is MainViewModel main)
                    main.MenuPart.MoveInMenu(entry, onto, below);
            },
            Label = item => (item as MenuEntry)?.Title ?? "",
        };
        // Groups run down a column for as far as the window has room, then start the next one.
        SizeChanged += (_, e) => Columns.MaxHeight = Math.Max(280, e.NewSize.Height - 330);
        Controls.DragReorder.Attach(Columns, rules);
        Controls.DragReorder.Attach(Available, rules);
    }

    // A click on the dimmed page behind the menu closes it; a click inside it doesn't reach the page.
    private void CloseMenu(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel main)
            main.MenuPart.IsMenuOpen = false;
    }

    private void KeepMenuOpen(object? sender, PointerPressedEventArgs e) => e.Handled = true;
}
