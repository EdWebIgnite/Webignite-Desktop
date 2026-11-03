using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Arrow keys on a list of rows: Down and Up move between the rows (Button.row) inside the element this is set
/// on, Enter opens the row, as a button does. From the search box above the list, Down goes to the first row.
/// Set <c>c:ListKeys.IsEnabled="True"</c> on the panel that holds the search box and the list.
/// </summary>
public static class ListKeys
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(ListKeys));

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    static ListKeys()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is true)
                control.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
            else
                control.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        });
    }

    private static void KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control root || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Down or Key.Up))
            return;
        var focused = TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() as Visual;
        // Up and Down still move the cursor in a box of several lines.
        if (focused is TextBox { AcceptsReturn: true })
            return;
        var rows = root.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("row") && b.IsEffectivelyVisible && b.IsEffectivelyEnabled)
            .ToList();
        if (rows.Count == 0)
            return;
        var at = focused is null ? -1 : rows.FindIndex(r => r == focused || r.IsVisualAncestorOf(focused));
        var next = at < 0
            ? (e.Key == Key.Down ? 0 : -1)
            : Math.Clamp(at + (e.Key == Key.Down ? 1 : -1), 0, rows.Count - 1);
        if (next < 0)
            return;
        rows[next].Focus(NavigationMethod.Directional);
        rows[next].BringIntoView();
        e.Handled = true;
    }
}
