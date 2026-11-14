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
            ForgetDrawings();
            ForgetWorlds();
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
        settled = true;
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
        ForgetDrawings();
        ForgetWorlds();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            Ask();
        else if (change.Property == SelectedIdProperty)
            LookAt(SelectedId);
        else if (change.Property == StarsProperty)
        {
            ForgetGone();
            names.Clear();
            // A picked client that was moved is followed to where it now is.
            if (SelectedId is not null)
                LookAt(SelectedId);
        }
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
            (toX, toY, toZoom) = (client.X * Wide, client.Y, Math.Clamp(0.45 / reach, 1.4, 4.5));
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

    // The layout fits a square, and the page is wider than it is tall: the planets are spread sideways to use the
    // width, so they sit further apart without being drawn any smaller. Moons keep their round paths.
    private double Wide => Bounds.Height <= 0 ? 1 : Math.Clamp(Bounds.Width / Bounds.Height * 0.97, 1, 1.8);

    private double Scale => Math.Min(Bounds.Width, Bounds.Height) / 2 * 0.9 * zoom;

    private Point OnScreen(double x, double y) =>
        new(Bounds.Width / 2 + (x - viewX) * Scale, Bounds.Height / 2 + (y - viewY) * Scale);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush("Nav"), bounds, 14);
        madeThisFrame = 0;
        var stars = Stars ?? [];
        DrawDust(context, bounds);
        shown.Clear();
        if (stars.Count == 0)
            return;

        var scale = Scale;
        var open = Ease(opened);
        var wide = Wide;
        var clients = stars.Where(p => p.Star.ParentId is null).ToDictionary(p => p.Star.Id);
        // A client being dragged is drawn where the pointer has it, its moons with it.
        if (dragging && dragId is not null && clients.TryGetValue(dragId, out var held))
            clients[dragId] = held with { X = dragX, Y = dragY };
        // What circles a client turns slowly round it; each client's ring at its own pace and direction.
        foreach (var star in stars)
        {
            if (star.Star.ParentId is { } parent && clients.TryGetValue(parent, out var centre))
            {
                var spin = (StableHash(parent) % 2 == 0 ? 1 : -1) * clock * (0.05 + (uint)StableHash(parent) % 5 * 0.012);
                var (dx, dy) = (star.X - centre.X, star.Y - centre.Y);
                var (cos, sin) = (Math.Cos(spin), Math.Sin(spin));
                shown.Add(star with { X = (centre.X * wide + dx * cos - dy * sin) * open, Y = (centre.Y + dx * sin + dy * cos) * open });
            }
            else
            {
                var me = clients[star.Star.Id];
                shown.Add(star with { X = me.X * wide * open, Y = me.Y * open });
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
            context.DrawLine(Line(ColourOf(Brush(ColourKey(star.Star))), (near ? 0.5 : 0.12) * open, 1),
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
                DrawPlanet(context, star.Star.Id, at, radius, colour, strength, beat, StableHash(star.Star.Id));
            else
                DrawMoon(context, at, radius, colour, strength, beat);
            if (isClient && star.Star.State == StarState.Problem)
                context.DrawEllipse(null, Line(ColourOf(Brush("CategoryRed")), (0.45 + 0.55 * beat) * strength, 3), at, radius + 2 + 3 * beat, radius + 2 + 3 * beat);
            if (star.Star.Id == selected || star.Star.Id == hover)
                context.DrawEllipse(null, Line(ColourOf(label), 0.9, 1.5), at, radius + 4, radius + 4);

            // Names: clients once they are big enough to carry one, what circles them once flown in or pointed at.
            var named = isClient ? radius >= 13 || star.Star.Id == hover : (focus == star.Star.ParentId && zoom > 1.3) || star.Star.Id == hover;
            if (named && open > 0.6)
            {
                // A name is laid out once and kept; it is only drawn fainter or stronger from frame to frame.
                var text = NameOf(star.Star, isClient, isClient ? Math.Clamp(radius * 0.3, 11, 16) : 11, isClient ? label : soft);
                using var faint = context.PushOpacity(Math.Clamp(strength, 0, 1));
                if (isClient)
                {
                    // A client's name sits below everything that circles it, so its moons never cover it.
                    var below = Math.Max(radius, reach.GetValueOrDefault(star.Star.Id) * scale);
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

    // Brushes, pens and laid-out names are made once and kept. Making them afresh for every dot on every frame
    // (hundreds of each, sixty times a second) is what made the map stutter; a frame is now nearly all drawing.
    private readonly Dictionary<uint, Avalonia.Media.Immutable.ImmutableSolidColorBrush> solids = [];
    private readonly Dictionary<(uint, int), IPen> lines = [];
    private readonly Dictionary<uint, IBrush> haloes = [];
    private readonly Dictionary<uint, IBrush> glows = [];
    private readonly Dictionary<(string, int), FormattedText> names = [];
    private IBrush? ballShade;

    private void ForgetDrawings()
    {
        solids.Clear();
        lines.Clear();
        haloes.Clear();
        glows.Clear();
        names.Clear();
        ballShade = null;
    }

    private static uint ColourKeyOf(Color colour, double opacity) =>
        (uint)((int)(Math.Clamp(opacity, 0, 1) * 31 + 0.5) << 24 | colour.R << 16 | colour.G << 8 | colour.B);

    // A plain colour at a strength, in 32 steps of strength, which is finer than the eye tells apart.
    private Avalonia.Media.Immutable.ImmutableSolidColorBrush Solid(Color colour, double opacity = 1)
    {
        var key = ColourKeyOf(colour, opacity);
        if (!solids.TryGetValue(key, out var brush))
            solids[key] = brush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(colour, (key >> 24) / 31.0);
        return brush;
    }

    private IPen Line(Color colour, double opacity, double thickness)
    {
        var key = (ColourKeyOf(colour, opacity), (int)(thickness * 4 + 0.5));
        if (!lines.TryGetValue(key, out var pen))
            lines[key] = pen = new Avalonia.Media.Immutable.ImmutablePen(Solid(colour, opacity), key.Item2 / 4.0);
        return pen;
    }

    private FormattedText NameOf(Star star, bool isClient, double size, IBrush colour)
    {
        var key = (star.Id, (int)(size * 2 + 0.5) * 2 + (isClient ? 1 : 0));
        if (names.TryGetValue(key, out var text) )
            return text;
        if (names.Count > 600)
            names.Clear();
        text = new FormattedText(star.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, (key.Item2 / 2) / 2.0, Solid(ColourOf(colour)))
        {
            MaxTextWidth = 190, MaxLineCount = isClient ? 2 : 1, TextAlignment = isClient ? TextAlignment.Center : TextAlignment.Left, Trimming = TextTrimming.CharacterEllipsis,
        };
        if (isClient)
            text.SetFontWeight(FontWeight.SemiBold);
        return names[key] = text;
    }

    // The lit side and the dark side of a ball lit from the upper left, as one brush laid over the ball: a soft
    // highlight where the light falls, then shadow growing towards the far edge until it is the colour of the sky.
    private IBrush BallShade()
    {
        if (ballShade is not null)
            return ballShade;
        var sky = ColourOf(Brush("Nav"));
        var light = ColourOf(Brush("NavText"));
        var lit = new RelativePoint(0.29, 0.29, RelativeUnit.Relative);
        return ballShade = new RadialGradientBrush
        {
            Center = lit, GradientOrigin = lit,
            GradientStops =
            {
                new GradientStop(Faint(light, 0.42), 0), new GradientStop(Faint(light, 0.08), 0.28), new GradientStop(Faint(sky, 0.1), 0.5),
                new GradientStop(Faint(sky, 0.62), 0.95), new GradientStop(Faint(sky, 0.95), 1.45), new GradientStop(Faint(sky, 0.97), 2),
            },
        }.ToImmutable();
    }

    // A glow that is strongest in the middle and gone at the edge; with <paramref name="hold"/>, strong most of the way out first.
    private static IBrush Fading(Color colour, double strength, double hold = 0)
    {
        var brush = new RadialGradientBrush { GradientStops = { new GradientStop(Faint(colour, strength), 0) } };
        if (hold > 0)
            brush.GradientStops.Add(new GradientStop(Faint(colour, strength * 0.9), hold));
        brush.GradientStops.Add(new GradientStop(Faint(colour, 0), 1));
        return brush.ToImmutable();
    }

    private void Shade(DrawingContext context, Point at, double radius) => context.DrawEllipse(BallShade(), null, at, radius, radius);

    // A planet's picture is this many dots across, whatever size it is drawn; its map of the globe is MapWidth by MapHeight.
    private const int PlanetDots = 128, MapWidth = 256, MapHeight = 128;
    private static readonly double[] Leans = [-0.36, -0.12, 0.14, 0.38];
    private static readonly string[] LandColours = ["ProjectGreen", "ProjectYellowOrange", "ProjectYellowGreen", "ProjectOrange"];

    /// <summary>One client's world: its map, its picture, and how far it had turned when the picture was last drawn.</summary>
    private sealed class World(uint[] map, Avalonia.Media.Imaging.WriteableBitmap picture, int lean, double pace) : IDisposable
    {
        public uint[] Map { get; } = map;
        public Avalonia.Media.Imaging.WriteableBitmap Picture { get; } = picture;
        public int Lean { get; } = lean;
        public double Pace { get; } = pace;
        public double Drawn { get; set; } = double.NaN;
        public void Dispose() => Picture.Dispose();
    }

    private readonly Dictionary<string, World> worlds = [];
    private readonly PlanetSphere?[] spheres = new PlanetSphere?[Leans.Length];
    private readonly int[] dots = new int[PlanetDots * PlanetDots];
    private int madeThisFrame;
    // Once a picture of the map has been asked for, every world is made at once so none is missing from it.
    private bool settled;

    private static uint Packed(Color colour) => (uint)(colour.R << 16 | colour.G << 8 | colour.B);

    // A client's world, made the first time it is drawn. Only a few are made in any one frame, so opening the map
    // on a full studio doesn't hold the screen up; until its turn comes a planet is a plain lit ball.
    private World? WorldOf(string id, Color colour, int seed)
    {
        if (worlds.TryGetValue(id, out var world))
            return world;
        if (madeThisFrame >= 3 && !settled)
            return null;
        madeThisFrame++;
        var light = ColourOf(Brush("NavText"));
        var dark = ColourOf(Brush("Nav"));
        var land = ColourOf(Brush(LandColours[(int)((uint)seed / 5 % LandColours.Length)]));
        var kind = PlanetSurface.KindOf(seed);
        var map = PlanetSurface.Make(kind, seed, MapWidth, MapHeight, Packed(kind == PlanetKind.Mars ? Rust() : colour), Packed(light), Packed(dark), Packed(land));
        var picture = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(PlanetDots, PlanetDots), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        world = new World(map, picture, (int)((uint)seed / 7 % Leans.Length), 2.2 + (uint)seed % 5 * 0.6);
        worlds[id] = world;
        return world;
    }

    // Worlds of clients no longer on the map are let go.
    private void ForgetGone()
    {
        var here = (Stars ?? []).Select(p => p.Star.Id).ToHashSet();
        foreach (var gone in worlds.Keys.Where(id => !here.Contains(id)).ToList())
        {
            worlds[gone].Dispose();
            worlds.Remove(gone);
        }
    }

    // Mars is rust whoever the client is: the palette's red and orange, a little towards the dark of the sky.
    private Color Rust() => Mix(Mix(ColourOf(Brush("ProjectRed")), ColourOf(Brush("ProjectOrange")), 0.5), ColourOf(Brush("Nav")), 0.22);

    private void ForgetWorlds()
    {
        foreach (var world in worlds.Values)
            world.Dispose();
        worlds.Clear();
    }

    /// <summary>
    /// A client as a planet: a real globe, drawn dot by dot from a map of its surface (gas bands, seas and land
    /// under cloud, cratered rock, ice or desert), lit from the upper left with a soft edge between day and night
    /// and a thin shine of air at the rim, turning slowly on a leaning axis. Round it an atmosphere that breathes
    /// while there is work under way, and on some of the bigger ones a ring. Which world a client gets comes from
    /// its id, so it is always the same one.
    /// </summary>
    private void DrawPlanet(DrawingContext context, string id, Point at, double radius, Color colour, double strength, double beat, int seed)
    {
        var kind = PlanetSurface.KindOf(seed);
        if (kind == PlanetKind.Mars)
            colour = Rust();
        var pale = Mix(colour, ColourOf(Brush("NavText")), 0.45);
        var ringed = radius >= 22 && kind is PlanetKind.Gas or PlanetKind.Ice && (uint)seed / 3 % 2 == 0;
        var world = radius >= 5 ? WorldOf(id, colour, seed) : null;
        var lean = Leans[world?.Lean ?? 0];
        var tilt = Matrix.CreateTranslation(-at.X, -at.Y) * Matrix.CreateRotation(lean) * Matrix.CreateTranslation(at.X, at.Y);
        using var faded = context.PushOpacity(Math.Clamp(strength, 0, 1));

        // The atmosphere: a faint halo just outside the globe, brighter on the lit side. One brush per colour;
        // its breathing is how strongly it is drawn.
        var halo = radius * (1.32 + 0.07 * beat);
        if (!haloes.TryGetValue(ColourKeyOf(colour, 1), out var air))
            haloes[ColourKeyOf(colour, 1)] = air = Fading(colour, 0.56, hold: 0.74);
        using (context.PushOpacity((0.34 + 0.22 * beat) / 0.56))
            context.DrawEllipse(air, null, new Point(at.X - radius * 0.06, at.Y - radius * 0.06), halo, halo);

        if (ringed)
            DrawRing(context, at, radius, pale, tilt, behind: true);

        if (world is null)
        {
            // Too small to show a surface, or not made yet: a plain ball, lit the same way.
            context.DrawEllipse(Solid(colour), null, at, radius, radius);
            Shade(context, at, radius);
        }
        else
        {
            // Drawn again once it has turned an eighth of a pixel, which is finer than the eye sees move.
            var turned = Spinner.Still ? 0 : clock * world.Pace;
            if (double.IsNaN(world.Drawn) || Math.Abs(turned - world.Drawn) * radius * 2 / PlanetDots >= 0.12)
            {
                var sphere = spheres[world.Lean] ??= new PlanetSphere(PlanetDots, Leans[world.Lean], MapWidth, MapHeight);
                sphere.Render(world.Map, turned, Packed(ColourOf(Brush("Nav"))), Packed(pale), dots);
                using (var frame = world.Picture.Lock())
                    System.Runtime.InteropServices.Marshal.Copy(dots, 0, frame.Address, dots.Length);
                world.Drawn = turned;
            }
            // The picture has a dot of clear edge all round, so it is drawn a touch larger than the globe.
            var half = radius * PlanetDots / (PlanetDots - 2.0);
            context.DrawImage(world.Picture, new Rect(at.X - half, at.Y - half, half * 2, half * 2));
        }

        if (ringed)
            DrawRing(context, at, radius, pale, tilt, behind: false);
    }

    // A ring seen at a slant: the far half is drawn before the ball and the near half after it, so the ball sits in it.
    private void DrawRing(DrawingContext context, Point at, double radius, Color colour, Matrix tilt, bool behind)
    {
        using var tilted = context.PushTransform(tilt);
        var half = behind
            ? new Rect(at.X - radius * 2.4, at.Y - radius, radius * 4.8, radius)
            : new Rect(at.X - radius * 2.4, at.Y, radius * 4.8, radius);
        using var clipped = context.PushClip(half);
        // Several bands with gaps between, the outer ones fainter.
        (double Out, double Wide, double Bright)[] bands = [(1.42, 0.07, 0.5), (1.6, 0.16, 0.75), (1.84, 0.1, 0.6), (2.02, 0.04, 0.35)];
        foreach (var (out_, wide, bright) in bands)
            context.DrawEllipse(null, Line(colour, bright * (behind ? 0.5 : 0.9), radius * wide), at, radius * out_, radius * out_ * 0.27);
    }

    /// <summary>What circles a client, as a small moon: a faint glow, and a ball lit from the same side as its planet.</summary>
    private void DrawMoon(DrawingContext context, Point at, double radius, Color colour, double strength, double beat)
    {
        using var faded = context.PushOpacity(Math.Clamp(strength, 0, 1));
        var glow = radius * 2.4 * (1 + 0.12 * beat);
        if (!glows.TryGetValue(ColourKeyOf(colour, 1), out var shine))
            glows[ColourKeyOf(colour, 1)] = shine = Fading(colour, 0.5);
        using (context.PushOpacity(0.55 + 0.45 * beat))
            context.DrawEllipse(shine, null, at, glow, glow);
        context.DrawEllipse(Solid(colour), null, at, radius, radius);
        Shade(context, at, radius);
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
            context.DrawEllipse(Solid(speck, depth * (0.4 + 0.6 * twinkle)), null, new Point(px, py), 0.8 + i % 3 * 0.4, 0.8 + i % 3 * 0.4);
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
        if (dragId is not null)
        {
            var point = e.GetPosition(this);
            if (!dragging && Math.Abs(point.X - pressedAt.X) + Math.Abs(point.Y - pressedAt.Y) > DragSlack)
                dragging = true;
            if (dragging)
            {
                var (x, y) = InLayout(point);
                (dragX, dragY) = (Math.Clamp(x + grabX, -1, 1), Math.Clamp(y + grabY, -1, 1));
                Cursor = new Cursor(StandardCursorType.SizeAll);
                InvalidateVisual();
            }
            return;
        }
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

    /// <summary>A client dragged to a new place: its id and where its middle now is, in the layout's own square.</summary>
    public event Action<string, double, double>? Moved;

    // The client the button went down on, where, and how far its middle was from the pointer; it is being dragged
    // once the pointer has moved more than a few pixels, and until then it is a click.
    private string? dragId;
    private Point pressedAt;
    private double grabX, grabY, dragX, dragY;
    private bool dragging;
    private const double DragSlack = 5;

    // A point on the screen as a place in the layout's square (with the sideways spread taken back out).
    private (double X, double Y) InLayout(Point point) =>
        ((viewX + (point.X - Bounds.Width / 2) / Scale) / Wide, viewY + (point.Y - Bounds.Height / 2) / Scale);

    /// <summary>Drags a client to a place in the layout's square, as the pointer would; for trying it without a pointer.</summary>
    public void Drag(string clientId, double x, double y) => Moved?.Invoke(clientId, Math.Clamp(x, -1, 1), Math.Clamp(y, -1, 1));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        var under = Under(point);
        if (under is not null && e.ClickCount >= 2)
        {
            // A double click on a client flies in to it; on one of its moons it opens what the moon stands for.
            if (under.Star.ParentId is null)
                SetCurrentValue(SelectedIdProperty, under.Star.Id);
            else
                Opened?.Invoke(under.Star);
            (dragId, dragging) = (null, false);
            e.Pointer.Capture(null);
            return;
        }
        // A client can be dragged. One click on it does nothing else, so a drag is never mistaken for a pick.
        if (under is { Star.ParentId: null } && Scale > 0 && (Stars ?? []).FirstOrDefault(p => p.Star.Id == under.Star.Id) is { } client)
        {
            dragId = client.Star.Id;
            pressedAt = point;
            var (x, y) = InLayout(point);
            (grabX, grabY) = (client.X - x, client.Y - y);
            (dragX, dragY) = (client.X, client.Y);
            dragging = false;
            e.Pointer.Capture(this);
            return;
        }
        // A click on a moon picks it; one on empty sky flies back out.
        SetCurrentValue(SelectedIdProperty, under?.Star.Id);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragId is not { } id)
            return;
        // Noted before the pointer is let go: letting it go is itself "the drag was dropped" and clears these.
        var wasDragging = dragging;
        var (x, y) = (dragX, dragY);
        (dragId, dragging) = (null, false);
        e.Pointer.Capture(null);
        if (wasDragging)
            Moved?.Invoke(id, Math.Clamp(x, -1, 1), Math.Clamp(y, -1, 1));
        InvalidateVisual();
    }

    /// <summary>Where a dot is on the screen, within this control; for pointing at one without a person.</summary>
    public Point? PlaceOnScreen(string id) => shown.FirstOrDefault(p => p.Star.Id == id) is { } star ? OnScreen(star.X, star.Y) : null;

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        (dragId, dragging) = (null, false);
        InvalidateVisual();
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
