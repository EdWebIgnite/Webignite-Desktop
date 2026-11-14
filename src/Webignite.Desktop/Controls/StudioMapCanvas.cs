using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Webignite.Core.Studio;

namespace Webignite.Desktop.Controls;

/// <summary>
/// The Studio map, drawn straight onto a dark canvas: each client a planet, what is done for them its moons. Dots fly out from the middle when it opens, what circles a client turns slowly round it, dots with work
/// under way or a problem pulse, and a click on a client flies in to it. It only asks for frames while it is on
/// screen, and with Motion set to Off it stands still. Colours come from the palette.
/// </summary>
public sealed class StudioMapCanvas : Control
{
    public static readonly StyledProperty<IReadOnlyList<PlacedStar>?> StarsProperty =
        AvaloniaProperty.Register<StudioMapCanvas, IReadOnlyList<PlacedStar>?>(nameof(Stars));

    public static readonly StyledProperty<string?> SelectedIdProperty =
        AvaloniaProperty.Register<StudioMapCanvas, string?>(nameof(SelectedId), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>How long the dots take to fly out when the map opens, and to fly in to a client.</summary>
    private const double OpenSeconds = 1.4, FlySeconds = 0.7;

    // Where the view is looking (in the map's own square) and how far in, eased towards where it should be.
    private double viewX, viewY, zoom = 1, fromX, fromY, fromZoom = 1, toX, toY, toZoom = 1, flown = 1;
    private double opened, clock;
    private TimeSpan? last;
    private bool asked;
    private string? hover;
    private readonly Dictionary<string, IBrush> brushes = [];
    // Each dot's place this frame, as drawn, for finding what is under the pointer.
    private readonly List<PlacedStar> shown = [];

    static StudioMapCanvas()
    {
        AffectsRender<StudioMapCanvas>(StarsProperty, SelectedIdProperty);
        FocusableProperty.OverrideDefaultValue<StudioMapCanvas>(true);
    }

    public StudioMapCanvas()
    {
        ClipToBounds = true;
        // Dark mode swaps the palette: draw again with the new colours.
        ResourcesChanged += (_, _) =>
        {
            brushes.Clear();
            InvalidateVisual();
        };
    }

    public IReadOnlyList<PlacedStar>? Stars { get => GetValue(StarsProperty); set => SetValue(StarsProperty, value); }
    public string? SelectedId { get => GetValue(SelectedIdProperty); set => SetValue(SelectedIdProperty, value); }

    /// <summary>A double click on a dot: open what it stands for.</summary>
    public event Action<Star>? Opened;

    /// <summary>Jumps every movement to its end, so a picture of the map is the same each time.</summary>
    public void Settle()
    {
        opened = 1;
        flown = 1;
        (viewX, viewY, zoom) = (toX, toY, toZoom);
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Spinner.Still)
            Settle();
        Ask();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        last = null;
        brushes.Clear();
        shown.Clear();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            Ask();
        else if (change.Property == SelectedIdProperty)
            LookAt(SelectedId);
    }

    private void Ask()
    {
        if (asked || Spinner.Still || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } top)
            return;
        asked = true;
        top.RequestAnimationFrame(Tick);
    }

    private void Tick(TimeSpan now)
    {
        asked = false;
        if (last is { } before)
        {
            var passed = Math.Min(0.1, (now - before).TotalSeconds);
            clock += passed;
            opened = Math.Min(1, opened + passed / OpenSeconds);
            if (flown < 1)
            {
                flown = Math.Min(1, flown + passed / FlySeconds);
                var eased = Ease(flown);
                viewX = fromX + (toX - fromX) * eased;
                viewY = fromY + (toY - fromY) * eased;
                zoom = fromZoom + (toZoom - fromZoom) * eased;
            }
        }
        last = now;
        InvalidateVisual();
        if (IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not null)
            Ask();
        else
            last = null;
    }

    private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

    // Flies to a client (or to the client of what was picked) so it and its ring fill the view; nothing picked flies back out.
    private void LookAt(string? id)
    {
        var stars = Stars ?? [];
        var picked = stars.FirstOrDefault(p => p.Star.Id == id);
        var client = picked is { Star.ParentId: { } parent } ? stars.FirstOrDefault(p => p.Star.Id == parent) : picked;
        (fromX, fromY, fromZoom) = (viewX, viewY, zoom);
        if (client is null)
        {
            (toX, toY, toZoom) = (0, 0, 1);
        }
        else
        {
            var reach = client.Radius + 0.3 * Math.Max(client.Radius, 0.12) + stars.Where(p => p.Star.ParentId == client.Star.Id)
                .Select(p => Math.Sqrt(Math.Pow(p.X - client.X, 2) + Math.Pow(p.Y - client.Y, 2)) + p.Radius - client.Radius).DefaultIfEmpty(0).Max();
            (toX, toY, toZoom) = (client.X, client.Y, Math.Clamp(0.45 / reach, 1.4, 4.5));
        }
        flown = Spinner.Still || TopLevel.GetTopLevel(this) is null ? 1 : 0;
        if (flown >= 1)
            (viewX, viewY, zoom) = (toX, toY, toZoom);
        Ask();
        InvalidateVisual();
    }

    private IBrush Brush(string key)
    {
        if (brushes.TryGetValue(key, out var found))
            return found;
        var brush = this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush b ? b : Brushes.Gray;
        brushes[key] = brush;
        return brush;
    }

    private static Color ColourOf(IBrush brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;

    // A client keeps its own colour whatever its state (a problem shows as a red ring), so the map isn't a wall of red.
    private string ColourKey(Star star) => star.State == StarState.Problem && star.Kind != StarKind.Client ? "CategoryRed" : star.Kind switch
    {
        StarKind.Project => "ProjectYellowOrange",
        StarKind.Site => "ProjectAqua",
        StarKind.Domain => "ProjectPurple",
        StarKind.Quote => "ProjectHotPink",
        _ => StudioWall.ColourOf(star.Id),
    };

    // The same for a client every run, unlike string.GetHashCode.
    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in text)
                hash = hash * 31 + c;
            return hash;
        }
    }

    private double Scale => Math.Min(Bounds.Width, Bounds.Height) / 2 * 0.9 * zoom;

    private Point OnScreen(double x, double y) =>
        new(Bounds.Width / 2 + (x - viewX) * Scale, Bounds.Height / 2 + (y - viewY) * Scale);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush("Nav"), bounds, 14);
        var stars = Stars ?? [];
        DrawDust(context, bounds);
        shown.Clear();
        if (stars.Count == 0)
            return;

        var scale = Scale;
        var open = Ease(opened);
        var clients = stars.Where(p => p.Star.ParentId is null).ToDictionary(p => p.Star.Id);
        // What circles a client turns slowly round it; each client's ring at its own pace and direction.
        foreach (var star in stars)
        {
            if (star.Star.ParentId is { } parent && clients.TryGetValue(parent, out var centre))
            {
                var spin = (StableHash(parent) % 2 == 0 ? 1 : -1) * clock * (0.05 + (uint)StableHash(parent) % 5 * 0.012);
                var (dx, dy) = (star.X - centre.X, star.Y - centre.Y);
                var (cos, sin) = (Math.Cos(spin), Math.Sin(spin));
                shown.Add(star with { X = (centre.X + dx * cos - dy * sin) * open, Y = (centre.Y + dx * sin + dy * cos) * open });
            }
            else
            {
                shown.Add(star with { X = star.X * open, Y = star.Y * open });
            }
        }
        var places = shown.ToDictionary(p => p.Star.Id);
        var selected = SelectedId;
        var focus = selected is not null && places.TryGetValue(selected, out var pick) ? pick.Star.ParentId ?? pick.Star.Id : null;

        // The lines from a client out to what circles it.
        foreach (var star in shown.Where(p => p.Star.ParentId is not null))
        {
            if (!places.TryGetValue(star.Star.ParentId!, out var centre))
                continue;
            var near = focus is null || star.Star.ParentId == focus;
            context.DrawLine(new Pen(new SolidColorBrush(ColourOf(Brush(ColourKey(star.Star))), (near ? 0.5 : 0.12) * open), 1),
                OnScreen(centre.X, centre.Y), OnScreen(star.X, star.Y));
        }

        var label = Brush("NavText");
        var soft = Brush("NavMuted");
        // How far out each client's moons reach, to put its name clear of them.
        var reach = new Dictionary<string, double>();
        foreach (var moon in shown.Where(p => p.Star.ParentId is not null))
        {
            if (!places.TryGetValue(moon.Star.ParentId!, out var centre))
                continue;
            var far = Math.Sqrt(Math.Pow(moon.X - centre.X, 2) + Math.Pow(moon.Y - centre.Y, 2)) + moon.Radius;
            if (far > reach.GetValueOrDefault(centre.Star.Id))
                reach[centre.Star.Id] = far;
        }
        foreach (var star in shown.OrderByDescending(p => p.Radius))
        {
            var at = OnScreen(star.X, star.Y);
            var radius = Math.Max(2.5, star.Radius * scale);
            if (at.X + radius * 3 < 0 || at.Y + radius * 3 < 0 || at.X - radius * 3 > bounds.Width || at.Y - radius * 3 > bounds.Height)
                continue;
            var colour = ColourOf(Brush(ColourKey(star.Star)));
            var isClient = star.Star.ParentId is null;
            var dimmed = focus is not null && (star.Star.ParentId ?? star.Star.Id) != focus;
            var strength = (dimmed ? 0.3 : 1) * open;
            // Work under way breathes; a problem beats faster.
            var beat = star.Star.State switch
            {
                StarState.Problem => 0.5 + 0.5 * Math.Sin(clock * 5),
                StarState.Busy => 0.5 + 0.5 * Math.Sin(clock * 1.6 + StableHash(star.Star.Id) % 7),
                _ => 0.35,
            };
            if (isClient)
                DrawPlanet(context, at, radius, colour, strength, beat, StableHash(star.Star.Id));
            else
                DrawMoon(context, at, radius, colour, strength, beat);
            if (isClient && star.Star.State == StarState.Problem)
                context.DrawEllipse(null, new Pen(new SolidColorBrush(ColourOf(Brush("CategoryRed")), (0.45 + 0.55 * beat) * strength), 3), at, radius + 2 + 3 * beat, radius + 2 + 3 * beat);
            if (star.Star.Id == selected || star.Star.Id == hover)
                context.DrawEllipse(null, new Pen(new SolidColorBrush(ColourOf(label), 0.9), 1.5), at, radius + 4, radius + 4);

            // Names: clients once they are big enough to carry one, what circles them once flown in or pointed at.
            var named = isClient ? radius >= 13 || star.Star.Id == hover : (focus == star.Star.ParentId && zoom > 1.3) || star.Star.Id == hover;
            if (named && open > 0.6)
            {
                var text = new FormattedText(star.Star.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default,
                    isClient ? Math.Clamp(radius * 0.3, 11, 16) : 11, new SolidColorBrush(ColourOf(isClient ? label : soft), strength))
                {
                    MaxTextWidth = 190, MaxLineCount = isClient ? 2 : 1, TextAlignment = isClient ? TextAlignment.Center : TextAlignment.Left, Trimming = TextTrimming.CharacterEllipsis,
                };
                if (isClient)
                {
                    // A client's name sits below everything that circles it, so its moons never cover it.
                    var below = Math.Max(radius, reach.GetValueOrDefault(star.Star.Id) * scale);
                    text.SetFontWeight(FontWeight.SemiBold);
                    context.DrawText(text, new Point(at.X - 95, at.Y + below + 7));
                }
                else
                {
                    // A moon's name goes on the side away from its planet, so it never lies across it.
                    var left = places.TryGetValue(star.Star.ParentId!, out var planet) && star.X < planet.X;
                    context.DrawText(text, new Point(left ? at.X - radius - 7 - text.Width : at.X + radius + 7, at.Y - text.Height / 2));
                }
            }
        }
    }

    private static Color Mix(Color a, Color b, double share) => Color.FromArgb(255,
        (byte)(a.R + (b.R - a.R) * share), (byte)(a.G + (b.G - a.G) * share), (byte)(a.B + (b.B - a.B) * share));

    private static Color Faint(Color colour, double opacity) => Color.FromArgb((byte)(255 * Math.Clamp(opacity, 0, 1)), colour.R, colour.G, colour.B);

    // The lit side and the dark side of a ball lit from the upper left: a soft highlight, then shadow growing
    // towards the far edge until it is the colour of the sky behind. Drawn inside a clip of the ball's own circle.
    private void Shade(DrawingContext context, Point at, double radius)
    {
        var sky = ColourOf(Brush("Nav"));
        var light = ColourOf(Brush("NavText"));
        var lit = new Point(at.X - radius * 0.42, at.Y - radius * 0.42);
        context.DrawEllipse(new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Faint(sky, 0), 0), new GradientStop(Faint(sky, 0), 0.36), new GradientStop(Faint(sky, 0.7), 0.62), new GradientStop(Faint(sky, 0.97), 0.84), new GradientStop(Faint(sky, 0.99), 1) },
        }, null, lit, radius * 1.95, radius * 1.95);
        context.DrawEllipse(new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Faint(light, 0.42), 0), new GradientStop(Faint(light, 0), 1) },
        }, null, lit, radius * 0.8, radius * 0.8);
    }

    /// <summary>
    /// A client as a planet: an atmosphere that glows (and breathes while there is work under way), a ball lit from
    /// the upper left with its far side in shadow, and a surface of its own, either cloud bands with a storm, seas
    /// with land, or craters, that turns slowly. The biggest of some kinds wear a ring. Which a client gets comes
    /// from its id, so it is always the same planet.
    /// </summary>
    private void DrawPlanet(DrawingContext context, Point at, double radius, Color colour, double strength, double beat, int seed)
    {
        var kind = (int)((uint)seed % 3);
        var pale = Mix(colour, ColourOf(Brush("NavText")), 0.45);
        var deep = Mix(colour, ColourOf(Brush("Nav")), 0.45);
        var ringed = radius >= 22 && (uint)seed / 3 % 3 == 0;
        var tilt = Matrix.CreateTranslation(-at.X, -at.Y) * Matrix.CreateRotation(-0.38 + (uint)seed % 5 * 0.07) * Matrix.CreateTranslation(at.X, at.Y);
        using var faded = context.PushOpacity(Math.Clamp(strength, 0, 1));

        // The atmosphere: a halo just outside the ball.
        var halo = radius * (1.5 + 0.1 * beat);
        context.DrawEllipse(new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Faint(colour, 0.5 + 0.3 * beat), 0), new GradientStop(Faint(colour, 0.42 + 0.3 * beat), 0.62), new GradientStop(Faint(colour, 0), 1) },
        }, null, at, halo, halo);

        if (ringed)
            DrawRing(context, at, radius, pale, tilt, behind: true);

        var ball = new EllipseGeometry(new Rect(at.X - radius, at.Y - radius, radius * 2, radius * 2));
        using (context.PushGeometryClip(ball))
        {
            context.DrawEllipse(new SolidColorBrush(colour), null, at, radius, radius);
            // The surface slides sideways, slowly, and comes round again: the planet turning.
            var turn = clock * (4 + (uint)seed % 4) % (radius * 4);
            using (context.PushTransform(tilt))
            {
                var dice = (uint)seed * 2654435761u;
                double Roll()
                {
                    dice = dice * 1664525u + 1013904223u;
                    return (dice >> 8) % 10000 / 10000.0;
                }
                if (kind == 0)
                {
                    // Cloud bands, some paler and some deeper, with one long storm.
                    var y = at.Y - radius;
                    for (var i = 0; y < at.Y + radius; i++)
                    {
                        var thick = radius * (0.16 + 0.2 * Roll());
                        context.FillRectangle(new SolidColorBrush(i % 2 == 0 ? pale : deep, 0.3 + 0.35 * Roll()), new Rect(at.X - radius * 1.3, y, radius * 2.6, thick));
                        y += thick + radius * 0.06 * Roll();
                    }
                    var stormX = at.X - radius * 2 + (radius * 1.2 + turn) % (radius * 4);
                    context.DrawEllipse(new SolidColorBrush(pale, 0.75), null, new Point(stormX, at.Y + radius * 0.3), radius * 0.3, radius * 0.14);
                }
                else
                {
                    // Land on a sea, or craters on rock: blobs scattered over twice the width, so there is always more coming round.
                    var count = kind == 1 ? 9 : 12;
                    for (var i = 0; i < count; i++)
                    {
                        var x = at.X - radius * 2 + (Roll() * radius * 4 + turn) % (radius * 4);
                        var y = at.Y + (Roll() * 2 - 1) * radius * 0.85;
                        var size = radius * (kind == 1 ? 0.18 + 0.3 * Roll() : 0.07 + 0.14 * Roll());
                        if (kind == 1)
                        {
                            context.DrawEllipse(new SolidColorBrush(pale, 0.8), null, new Point(x, y), size * 1.5, size);
                            context.DrawEllipse(new SolidColorBrush(pale, 0.8), null, new Point(x + size * 0.9, y + size * 0.5), size * 0.8, size * 0.7);
                        }
                        else
                        {
                            context.DrawEllipse(new SolidColorBrush(deep, 0.85), new Pen(new SolidColorBrush(pale, 0.5), Math.Max(0.6, size * 0.18)), new Point(x, y), size, size);
                        }
                    }
                }
            }
            Shade(context, at, radius);
        }
        // A thin bright edge where the light catches the air.
        context.DrawEllipse(null, new Pen(new SolidColorBrush(pale, 0.35), 1), at, radius, radius);

        if (ringed)
            DrawRing(context, at, radius, pale, tilt, behind: false);
    }

    // A ring seen at a slant: the far half is drawn before the ball and the near half after it, so the ball sits in it.
    private static void DrawRing(DrawingContext context, Point at, double radius, Color colour, Matrix tilt, bool behind)
    {
        using var tilted = context.PushTransform(tilt);
        var half = behind
            ? new Rect(at.X - radius * 2.4, at.Y - radius, radius * 4.8, radius)
            : new Rect(at.X - radius * 2.4, at.Y, radius * 4.8, radius);
        using var clipped = context.PushClip(half);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(colour, behind ? 0.3 : 0.6), radius * 0.2), at, radius * 1.85, radius * 0.48);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(colour, behind ? 0.2 : 0.4), radius * 0.07), at, radius * 1.52, radius * 0.39);
    }

    /// <summary>What circles a client, as a small moon: a faint glow, and a ball lit from the same side as its planet.</summary>
    private void DrawMoon(DrawingContext context, Point at, double radius, Color colour, double strength, double beat)
    {
        using var faded = context.PushOpacity(Math.Clamp(strength, 0, 1));
        var glow = radius * 2.4 * (1 + 0.12 * beat);
        context.DrawEllipse(new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Faint(colour, 0.5 * (0.55 + 0.45 * beat)), 0), new GradientStop(Faint(colour, 0), 1) },
        }, null, at, glow, glow);
        using (context.PushGeometryClip(new EllipseGeometry(new Rect(at.X - radius, at.Y - radius, radius * 2, radius * 2))))
        {
            context.DrawEllipse(new SolidColorBrush(colour), null, at, radius, radius);
            Shade(context, at, radius);
        }
    }

    // A scatter of faint specks behind everything, the same each time, drifting a little as the view moves.
    private void DrawDust(DrawingContext context, Rect bounds)
    {
        var speck = ColourOf(Brush("NavText"));
        var seed = 12345u;
        for (var i = 0; i < 90; i++)
        {
            seed = seed * 1664525u + 1013904223u;
            var x = (seed >> 8) % 10000 / 10000.0;
            seed = seed * 1664525u + 1013904223u;
            var y = (seed >> 8) % 10000 / 10000.0;
            var depth = 0.15 + i % 5 * 0.08;
            var px = ((x - viewX * depth * 0.2) % 1 + 1) % 1 * bounds.Width;
            var py = ((y - viewY * depth * 0.2) % 1 + 1) % 1 * bounds.Height;
            var twinkle = 0.5 + 0.5 * Math.Sin(clock * (0.6 + i % 7 * 0.2) + i);
            context.DrawEllipse(new SolidColorBrush(speck, depth * (0.4 + 0.6 * twinkle)), null, new Point(px, py), 0.8 + i % 3 * 0.4, 0.8 + i % 3 * 0.4);
        }
    }

    private PlacedStar? Under(Point point)
    {
        var scale = Scale;
        if (scale <= 0)
            return null;
        var x = viewX + (point.X - Bounds.Width / 2) / scale;
        var y = viewY + (point.Y - Bounds.Height / 2) / scale;
        // A few pixels of slack, as what circles a client is small.
        return StudioMap.At(shown, x, y, 5 / scale);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var under = Under(e.GetPosition(this))?.Star.Id;
        if (under == hover)
            return;
        hover = under;
        Cursor = under is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var under = Under(e.GetPosition(this));
        if (under is not null && e.ClickCount >= 2)
        {
            Opened?.Invoke(under.Star);
            return;
        }
        // A click on empty sky flies back out.
        SetCurrentValue(SelectedIdProperty, under?.Star.Id);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && SelectedId is not null)
        {
            SetCurrentValue(SelectedIdProperty, null);
            e.Handled = true;
        }
    }
}
