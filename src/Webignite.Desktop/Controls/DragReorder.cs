using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Drag to reorder, the same way everywhere, as in Asana: press anywhere on a row, card or tile and move a few
/// pixels; a small card with its name follows the pointer, the row itself dims, and a line shows where it will land (a ring
/// when it would go into something, such as a board column). Esc puts it back. A plain click still clicks.
/// Dragging past the list onto a <see cref="DropZones">drop zone</see> elsewhere, such as a project in the
/// sidebar, drops it there instead. Rows are the containers of an items control, found by their data.
/// </summary>
public sealed class DragReorder
{
    private const double Threshold = 5;
    private const double EdgeScroll = 32;

    private readonly Control host;
    private Control? pressedOn;
    private object? pressedItem;
    private Point pressedAt;
    private bool held;
    private Drag? drag;

    private readonly DragRules rules;

    private DragReorder(Control host, DragRules rules)
    {
        this.host = host;
        this.rules = rules;
        host.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Cancel(), RoutingStrategies.Bubble);
    }

    /// <summary>Makes a control's rows draggable, by the rules given.</summary>
    public static DragReorder Attach(Control host, DragRules rules) => new(host, rules);

    /// <summary>True while something is being dragged in this host.</summary>
    public bool IsDragging => drag is not null;

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (drag is not null || !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            return;
        // A box being typed in keeps its own dragging, to pick words; one that isn't, such as a task's name, drags the row.
        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>(includeSelf: true) is { IsFocused: true })
            return;
        // With a handle set, only a press on it picks the row up, so rows can hold other draggable things.
        if (rules.Handle is { } handle && (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<StyledElement>().Any(v => v.Classes.Contains(handle)) != true)
            return;
        var (container, item) = ItemAt(e.Source as Visual, rules.CanDrag);
        if (container is null || item is null)
            return;
        pressedOn = container;
        pressedItem = item;
        pressedAt = e.GetPosition(host);
        // In a list box, hold the press back so it doesn't pick the row; a release without a drag picks it.
        held = rules.Click is not null && (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is null;
        if (held)
            e.Handled = true;
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (pressedOn is null || pressedItem is null)
            return;
        var at = e.GetPosition(host);
        if (drag is null)
        {
            if (Math.Abs(at.X - pressedAt.X) < Threshold && Math.Abs(at.Y - pressedAt.Y) < Threshold)
                return;
            if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            {
                Forget();
                return;
            }
            drag = Drag.Begin(host, pressedOn, pressedItem, rules.Label(pressedItem));
            if (drag is null)
            {
                Forget();
                return;
            }
            e.Pointer.Capture(host);
        }
        drag.Follow(e.GetPosition(drag.Layer));
        ScrollNearEdge(e);
        Aim(e);
        e.Handled = true;
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (drag is { } going)
        {
            var (target, below, zone) = (going.Target, going.Below, going.Zone);
            End();
            e.Pointer.Capture(null);
            e.Handled = true;
            if (zone is not null && target is not null)
                zone.Drop(going.Item, target);
            else if (target is not null)
                rules.Drop(going.Item, target, below);
            return;
        }
        if (held && pressedItem is { } item && e.InitialPressMouseButton == MouseButton.Left)
        {
            rules.Click?.Invoke(item);
            e.Handled = true;
        }
        Forget();
    }

    /// <summary>Stops a drag, leaving everything where it was.</summary>
    public void Cancel()
    {
        if (drag is not null)
            End();
    }

    private void End()
    {
        drag?.Dispose();
        drag = null;
        Forget();
    }

    private void Forget()
    {
        pressedOn = null;
        pressedItem = null;
        held = false;
    }

    // What's under the pointer: a row of this host, or a drop zone elsewhere in the window.
    private void Aim(PointerEventArgs e)
    {
        var going = drag!;
        var point = e.GetPosition(host);
        if (new Rect(host.Bounds.Size).Contains(point))
        {
            var (container, item) = ItemAt(host.InputHitTest(point) as Visual, _ => true);
            if (container is null)
                (container, item) = ItemUnder(host, point);
            if (container is not null && item is not null && !ReferenceEquals(item, going.Item))
            {
                if (rules.IsInto(going.Item, item))
                {
                    if (rules.CanDrop(going.Item, item, true))
                    {
                        going.Ring(container, item, true, null);
                        return;
                    }
                }
                else
                {
                    var origin = container.TranslatePoint(default, host) ?? default;
                    var across = rules.Across(item);
                    var below = across ? point.X > origin.X + container.Bounds.Width / 2 : point.Y > origin.Y + container.Bounds.Height / 2;
                    if (rules.CanDrop(going.Item, item, below))
                    {
                        going.Line(container, item, below, across);
                        return;
                    }
                }
            }
            going.Clear();
            return;
        }
        if (DropZones.Find(host, e, going.Item) is { } found)
        {
            going.Ring(found.Container, found.Item, true, found.Zone);
            return;
        }
        going.Clear();
    }

    private void ScrollNearEdge(PointerEventArgs e)
    {
        if (host.FindAncestorOfType<ScrollViewer>(includeSelf: true) is not { } scroller)
            return;
        var y = e.GetPosition(scroller).Y;
        var step = y < EdgeScroll ? -14 : y > scroller.Bounds.Height - EdgeScroll ? 14 : 0;
        if (step != 0)
            scroller.Offset = scroller.Offset.WithY(Math.Clamp(scroller.Offset.Y + step, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height)));
    }

    /// <summary>The innermost row under a point whose data passes the test: a child of an items panel, or of the host itself.</summary>
    internal (Control? Container, object? Item) ItemAt(Visual? from, Func<object, bool> test) => FindItem(host, from, test);

    // Over a gap no pointer can hit (a heading's space above it, say): the row whose box holds the point, innermost first.
    private static (Control? Container, object? Item) ItemUnder(Control host, Point point)
    {
        (Control?, object?) found = (null, null);
        foreach (var panel in host.GetVisualDescendants().OfType<Panel>().Where(p => p.IsItemsHost && p.IsEffectivelyVisible))
        {
            foreach (var child in panel.Children)
            {
                if (child.DataContext is not { } data || child.TranslatePoint(default, host) is not { } origin)
                    continue;
                if (new Rect(origin, child.Bounds.Size).Contains(point))
                    found = (child, data);
            }
        }
        return found;
    }

    public static (Control? Container, object? Item) FindItem(Control host, Visual? from, Func<object, bool> test)
    {
        for (var visual = from; visual is not null && visual != host; visual = visual.GetVisualParent())
        {
            if (visual is Control { DataContext: { } data } control && control.GetVisualParent() is Panel { IsItemsHost: true } && test(data))
                return (control, data);
        }
        return (null, null);
    }

    // One drag in progress: the card under the pointer, the dimmed row, and the mark where it would land.
    private sealed class Drag : IDisposable
    {
        private readonly Control row;
        private readonly double rowOpacity;
        private readonly Canvas canvas;
        private readonly AdornerLayer adorners;
        private readonly Border mark;

        private Drag(Control row, object item, string label, AdornerLayer adorners, Visual layer)
        {
            this.row = row;
            this.adorners = adorners;
            Item = item;
            Layer = layer;
            rowOpacity = row.Opacity;
            // As Asana's: a small card with the name, a thin outline and a soft shadow, held just below the pointer.
            var words = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320, FontWeight = FontWeight.SemiBold };
            words.Bind(TextBlock.ForegroundProperty, words.GetResourceObservable("Text"));
            Card = new Border
            {
                Child = words,
                Padding = new Thickness(12, 7),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Opacity = 0.95,
            };
            Card.Bind(Border.BackgroundProperty, Card.GetResourceObservable("Surface"));
            Card.Bind(Border.BorderBrushProperty, Card.GetResourceObservable("DropMark"));
            Card.Bind(Border.BoxShadowProperty, Card.GetResourceObservable("PopupShadow"));
            mark = new Border { IsVisible = false, IsHitTestVisible = false, CornerRadius = new CornerRadius(2) };
            mark.Bind(Border.BorderBrushProperty, mark.GetResourceObservable("DropMark"));
            canvas = new Canvas { IsHitTestVisible = false, Children = { mark, Card } };
            AdornerLayer.SetAdornedElement(canvas, layer);
            AdornerLayer.SetIsClipEnabled(canvas, false);
            adorners.Children.Add(canvas);
            row.Opacity = 0.35;
        }

        public object Item { get; }
        public Visual Layer { get; }
        private Border Card { get; }
        public object? Target { get; private set; }
        public bool Below { get; private set; }
        public DropZone? Zone { get; private set; }

        public static Drag? Begin(Control host, Control row, object item, string label)
        {
            if (TopLevel.GetTopLevel(host) is not { } top || AdornerLayer.GetAdornerLayer(host) is not { } adorners)
                return null;
            // A press on a task's name put the cursor in it; a drag isn't typing, so let it go.
            if (top.FocusManager?.GetFocusedElement() is TextBox { } typing && row.IsVisualAncestorOf(typing))
                top.FocusManager.Focus(null, NavigationMethod.Unspecified, KeyModifiers.None);
            // The card and the mark are drawn over the window's content, in its coordinates.
            var layer = (top as ContentControl)?.Content as Visual ?? top;
            return new Drag(row, item, label, adorners, layer);
        }

        public void Follow(Point at)
        {
            Canvas.SetLeft(Card, at.X + 12);
            Canvas.SetTop(Card, at.Y + 10);
        }

        public void Line(Control container, object item, bool below, bool across)
        {
            Target = item;
            Below = below;
            Zone = null;
            var origin = container.TranslatePoint(default, Layer) ?? default;
            var b = container.Bounds;
            mark.Background = mark.BorderBrush;
            mark.BorderThickness = default;
            mark.CornerRadius = new CornerRadius(2);
            if (across)
            {
                mark.Width = 3;
                mark.Height = b.Height;
                Canvas.SetLeft(mark, origin.X + (below ? b.Width : 0) - 1.5);
                Canvas.SetTop(mark, origin.Y);
            }
            else
            {
                mark.Width = b.Width;
                mark.Height = 2;
                Canvas.SetLeft(mark, origin.X);
                Canvas.SetTop(mark, origin.Y + (below ? b.Height : 0) - 1);
            }
            mark.IsVisible = true;
        }

        public void Ring(Control container, object item, bool below, DropZone? zone)
        {
            Target = item;
            Below = below;
            Zone = zone;
            var origin = container.TranslatePoint(default, Layer) ?? default;
            mark.Background = Brushes.Transparent;
            mark.BorderThickness = new Thickness(2);
            mark.CornerRadius = new CornerRadius(6);
            mark.Width = container.Bounds.Width;
            mark.Height = container.Bounds.Height;
            Canvas.SetLeft(mark, origin.X);
            Canvas.SetTop(mark, origin.Y);
            mark.IsVisible = true;
        }

        public void Clear()
        {
            Target = null;
            Zone = null;
            mark.IsVisible = false;
        }

        public void Dispose()
        {
            row.Opacity = rowOpacity;
            adorners.Children.Remove(canvas);
        }
    }
}

/// <summary>What can be dragged in a list, where it can land, and what landing does.</summary>
public sealed record DragRules
{
    /// <summary>What can be picked up.</summary>
    public Func<object, bool> CanDrag { get; init; } = _ => true;

    /// <summary>
    /// A style class: when set, a row is picked up only by a press on an element with it (a widget's heading, say),
    /// so the row can hold things that drag on their own.
    /// </summary>
    public string? Handle { get; init; }

    /// <summary>What a row can land next to: the dragged item, the one under the pointer, and whether below (or after) it.</summary>
    public Func<object, object, bool, bool> CanDrop { get; init; } = (_, _, _) => true;

    /// <summary>Puts it there.</summary>
    public Action<object, object, bool> Drop { get; init; } = (_, _, _) => { };

    /// <summary>For what's dragged, the items it goes into rather than next to, marked with a ring: a board column for a card, say.</summary>
    public Func<object, object, bool> IsInto { get; init; } = (_, _) => false;

    /// <summary>For an item, whether above or below is decided by left and right rather than up and down (tiles in a grid, columns in a row).</summary>
    public Func<object, bool> Across { get; init; } = _ => false;

    /// <summary>Only for a list box: a press doesn't pick until it turns out to be a click, so a drag doesn't open the page.</summary>
    public Action<object>? Click { get; init; }

    /// <summary>The words on the card that follows the pointer: the task's or page's name.</summary>
    public Func<object, string> Label { get; init; } = item => item.ToString() ?? "";
}

/// <summary>
/// A place outside a list that takes something dragged from it: what it takes, and what dropping does. Its rows
/// take things one by one; with <see cref="Whole"/>, the place as a whole takes them, its data being the target.
/// </summary>
public sealed record DropZone(Control Host, Func<object, object, bool> Takes, Action<object, object> Drop)
{
    public bool Whole { get; init; }
}

/// <summary>
/// The window's drop zones, such as the projects in the sidebar taking a task dragged from a project's list. A
/// zone is a control whose rows (containers of its items) can each take something; it goes when its window does.
/// </summary>
public static class DropZones
{
    private static readonly List<DropZone> Zones = [];

    /// <summary>Takes things while its control is on screen; a page shown again takes them again.</summary>
    public static void Add(DropZone zone)
    {
        if (TopLevel.GetTopLevel(zone.Host) is not null)
            Zones.Add(zone);
        zone.Host.AttachedToVisualTree += (_, _) =>
        {
            if (!Zones.Contains(zone))
                Zones.Add(zone);
        };
        zone.Host.DetachedFromVisualTree += (_, _) => Zones.Remove(zone);
    }

    internal static (DropZone Zone, Control Container, object Item)? Find(Control from, PointerEventArgs e, object dragged)
    {
        var top = TopLevel.GetTopLevel(from);
        foreach (var zone in Zones)
        {
            if (TopLevel.GetTopLevel(zone.Host) != top || !zone.Host.IsEffectivelyVisible)
                continue;
            var point = e.GetPosition(zone.Host);
            if (!new Rect(zone.Host.Bounds.Size).Contains(point))
                continue;
            if (zone.Whole)
            {
                if (zone.Host.DataContext is { } data && zone.Takes(dragged, data))
                    return (zone, zone.Host, data);
                continue;
            }
            var (container, item) = DragReorder.FindItem(zone.Host, zone.Host.InputHitTest(point) as Visual, data => zone.Takes(dragged, data));
            if (container is not null && item is not null)
                return (zone, container, item);
        }
        return null;
    }
}
