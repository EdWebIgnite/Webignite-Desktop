using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Webignite.Core.Layout;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Colours for any card in the app, picked by the person: a right-click on a card's heading, or on the empty edge
/// of a card, opens the same pop-up the widgets on their own pages use. Nothing is asked of the pages: every card
/// (a Border with the class "card") is known by where it sits in its page, and its colours are kept under that
/// name in <see cref="AppSettings.TileLooks"/>.
///
/// A card is one Border with no separate heading part, so its colours are painted as its background: the heading
/// colour down to just under its heading, the body colour from there, and a line along the top when one is picked.
/// A card that was never coloured is not touched, so the app looks exactly as it did until the person colours one.
/// </summary>
public static class TileColours
{
    private static readonly AttachedProperty<bool> ColouredProperty = AvaloniaProperty.RegisterAttached<Border, bool>("Coloured", typeof(TileColours));

    private static Dictionary<string, TileLook>? looks;
    private static AppServices? readFor;
    private static bool watching;

    /// <summary>Starts watching for cards coming on screen and for right-clicks on them. Called once, when the app starts.</summary>
    public static void Watch()
    {
        if (watching)
            return;
        watching = true;
        Control.LoadedEvent.AddClassHandler<Border>(Loaded, RoutingStrategies.Direct);
        Control.ContextRequestedEvent.AddClassHandler<Border>(Asked, RoutingStrategies.Bubble);
    }

    private static bool IsCard(Border border) => border.Classes.Contains("card") || border.Classes.Contains("overviewcard");

    // The colours kept, read once for the services in use (the screenshot tool and the tests each have their own).
    private static Dictionary<string, TileLook> Looks()
    {
        if (looks is null || !ReferenceEquals(readFor, App.Services))
        {
            readFor = App.Services;
            looks = App.Services is { } services ? new(services.Settings.TileLooks, StringComparer.Ordinal) : [];
        }
        return looks;
    }

    private static void Loaded(Border border, RoutedEventArgs e)
    {
        // Until a card has been coloured there is nothing to do for any card, which is how the app starts.
        if (IsCard(border) && Looks().Count > 0)
            Paint(border);
    }

    private static void Asked(Border border, ContextRequestedEventArgs e)
    {
        if (e.Handled || !IsCard(border) || e.Source is not Visual clicked)
            return;
        // Only the card's own edge or its heading: a right-click on what is in the card belongs to that.
        var onHeading = clicked is TextBlock text && ReferenceEquals(text, Heading(border));
        if (!ReferenceEquals(clicked, border) && !onHeading)
            return;
        Show(border);
        e.Handled = true;
    }

    /// <summary>Opens the colour pop-up for a card.</summary>
    public static void Show(Border card)
    {
        var flyout = new Flyout { Placement = PlacementMode.Pointer };
        flyout.Content = Picker(card, flyout.Hide);
        flyout.ShowAt(card);
    }

    /// <summary>What the colour pop-up shows for a card. Also for the screenshot tool, which can't draw pop-ups.</summary>
    public static Control Picker(Border card, Action close) =>
        TilePicker.Build(LookOf(card), look => Wear(card, look), close,
            App.Services is { } services ? picked => services.Go.PickColour(picked) : null, hasHeading: Heading(card) is not null);

    /// <summary>The colours a card has now; null when it is plain.</summary>
    public static TileLook? LookOf(Border card) => Looks().GetValueOrDefault(Key(card));

    /// <summary>Gives a card its colours (null makes it plain again), keeps them, and paints every card on screen known by the same name.</summary>
    public static void Wear(Border card, TileLook? look)
    {
        var key = Key(card);
        var all = Looks();
        if (TileLook.Tidied(look) is { } tidy)
            all[key] = tidy;
        else
            all.Remove(key);
        if (App.Services is { } services)
            services.Settings.TileLooks = all;
        // Cards drawn from one template (a row of days, a list of clients) share a name, and so their colours.
        Visual root = TopLevel.GetTopLevel(card) is { } top ? top : card;
        foreach (var same in root.GetVisualDescendants().OfType<Border>().Where(b => IsCard(b) && Key(b) == key).Append(card).Distinct())
            Paint(same);
    }

    private static void Paint(Border card)
    {
        var look = LookOf(card);
        if (look is null)
        {
            if (!card.GetValue(ColouredProperty))
                return;
            card.ClearValue(Border.BackgroundProperty);
            card.ClearValue(Border.BorderBrushProperty);
            card.ClearValue(ColouredProperty);
            return;
        }
        if (!card.GetValue(ColouredProperty))
        {
            card.SetValue(ColouredProperty, true);
            // Another look (light to dark, another palette) changes what the colours are mixed into; a card that
            // changes height moves where its heading ends.
            card.ActualThemeVariantChanged += (_, _) => Paint(card);
            card.SizeChanged += (_, _) => Paint(card);
        }
        var brushes = new TileBrushes(card, look);
        var body = brushes.BodyColour ?? brushes.SurfaceColour;
        var head = brushes.HeadColour;
        var line = brushes.LineColour;
        var headEnds = head is null ? 0 : HeadingEnds(card);
        if (headEnds <= 0)
            head = null;
        if (head is null && line is null)
            card.Background = new SolidColorBrush(body);
        else
        {
            // One background, top to bottom: the line (3 high), the heading's colour to just under the heading,
            // then the body's for the rest. The end point is given in pixels, so the bands don't stretch with the card.
            const double lineHigh = 3;
            var ends = Math.Max(head is null ? lineHigh : headEnds, lineHigh);
            var stops = new GradientStops();
            var top = head ?? body;
            if (line is { } l)
            {
                stops.Add(new GradientStop(l, 0));
                stops.Add(new GradientStop(l, lineHigh / ends));
                stops.Add(new GradientStop(top, lineHigh / ends));
            }
            else
                stops.Add(new GradientStop(top, 0));
            stops.Add(new GradientStop(top, 1));
            stops.Add(new GradientStop(body, 1));
            card.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(0, ends, RelativeUnit.Absolute),
                GradientStops = stops,
            };
        }
        if (brushes.Edge is { } edge)
            card.BorderBrush = edge;
        else
            card.ClearValue(Border.BorderBrushProperty);
    }

    // The card's own heading: the first heading text in it that isn't inside a card of its own.
    private static TextBlock? Heading(Border card) =>
        card.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => (t.Classes.Contains("h2") || t.Classes.Contains("h1")) && ReferenceEquals(t.GetVisualAncestors().OfType<Border>().FirstOrDefault(IsCard), card));

    // How far down the card its heading ends, with a little air under it; 0 when it has none or isn't laid out yet.
    private static double HeadingEnds(Border card) =>
        Heading(card) is { IsVisible: true } heading && heading.TranslatePoint(new Point(0, heading.Bounds.Height), card) is { } foot && foot.Y > 0
            ? Math.Round(foot.Y + 10)
            : 0;

    /// <summary>
    /// The name a card is known by: the page it is on, what it shows, and the way down to it from the page, counted
    /// in children. A card inside a list counts as "any of them" at the list, so every row's card has one name. The
    /// name holds for as long as the page is laid out the same way; a page redesigned in an update starts plain.
    /// </summary>
    public static string Key(Border card)
    {
        var steps = new List<string>();
        Visual at = card;
        var page = "";
        while (at.GetVisualParent() is { } parent)
        {
            if (parent is UserControl view)
            {
                page = view.GetType().Name;
                break;
            }
            // Under a list's panel every child is one of its items, whichever it is.
            var listed = parent is Panel { IsItemsHost: true };
            steps.Add(listed ? "*" : parent.GetVisualChildren().TakeWhile(c => !ReferenceEquals(c, at)).Count().ToString(System.Globalization.CultureInfo.InvariantCulture));
            at = parent;
        }
        steps.Reverse();
        return $"{page}|{card.DataContext?.GetType().Name}|{string.Join('.', steps)}";
    }
}
