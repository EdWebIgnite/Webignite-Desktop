using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Keeps a heading row in view while its list scrolls under it, like the column titles on a long invoice list.
/// Set <c>c:Sticky.IsEnabled="True"</c> on the heading. It stays at the top of the page's scroll area until the
/// bottom of the panel it sits in comes past, then leaves with it. While held it gets the <c>:stuck</c>
/// pseudo-class, so a style can give it a shadow. It needs an opaque background, or the rows show through.
/// </summary>
public static class Sticky
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(Sticky));

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    private static readonly AttachedProperty<ScrollViewer?> ScrollerProperty =
        AvaloniaProperty.RegisterAttached<Control, ScrollViewer?>("Scroller", typeof(Sticky));

    private static readonly AttachedProperty<EventHandler<ScrollChangedEventArgs>?> HandlerProperty =
        AvaloniaProperty.RegisterAttached<Control, EventHandler<ScrollChangedEventArgs>?>("Handler", typeof(Sticky));

    static Sticky()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is true)
            {
                control.ZIndex = 1;
                control.AttachedToVisualTree += Attached;
                control.DetachedFromVisualTree += Detached;
                control.LayoutUpdated += Moved;
                if (control.IsAttachedToVisualTree())
                    Attach(control);
            }
            else
            {
                control.AttachedToVisualTree -= Attached;
                control.DetachedFromVisualTree -= Detached;
                control.LayoutUpdated -= Moved;
                Detach(control);
                Hold(control, 0);
            }
        });
    }

    private static void Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
            Attach(control);
    }

    private static void Detached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
            Detach(control);
    }

    private static void Attach(Control control)
    {
        Detach(control);
        var scroller = control.FindAncestorOfType<ScrollViewer>();
        if (scroller is null)
            return;
        EventHandler<ScrollChangedEventArgs> handler = (_, _) => Update(control);
        scroller.ScrollChanged += handler;
        control.SetValue(ScrollerProperty, scroller);
        control.SetValue(HandlerProperty, handler);
        Update(control);
    }

    private static void Detach(Control control)
    {
        if (control.GetValue(ScrollerProperty) is { } scroller && control.GetValue(HandlerProperty) is { } handler)
            scroller.ScrollChanged -= handler;
        control.SetValue(ScrollerProperty, null);
        control.SetValue(HandlerProperty, null);
    }

    private static void Moved(object? sender, EventArgs e)
    {
        if (sender is Control control)
            Update(control);
    }

    private static void Update(Control control)
    {
        var scroller = control.GetValue(ScrollerProperty);
        // The parent isn't moved by us, so its place is a true measure of how far the heading has scrolled.
        if (scroller is null || control.GetVisualParent() is not Visual parent || !control.IsVisible)
            return;
        if (parent.TranslatePoint(new Point(0, 0), scroller) is not { } at)
            return;
        var top = at.Y + control.Bounds.Y;
        var room = parent.Bounds.Height - control.Bounds.Bottom;
        Hold(control, Math.Clamp(-top, 0, Math.Max(0, room)));
    }

    private static void Hold(Control control, double shift)
    {
        var current = (control.RenderTransform as TranslateTransform)?.Y ?? 0;
        if (Math.Abs(current - shift) < 0.5)
            return;
        control.RenderTransform = shift > 0 ? new TranslateTransform(0, shift) : null;
        ((IPseudoClasses)control.Classes).Set(":stuck", shift > 0);
    }
}
