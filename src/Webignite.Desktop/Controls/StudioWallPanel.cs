using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Webignite.Core.Studio;

namespace Webignite.Desktop.Controls;

/// <summary>
/// Studio's wall: a card for every client on the dark canvas, each in its own colour with its initials, a light
/// for how things stand, and on its back what is being done for it. The cards rise into place one after the other
/// when it opens, and one turns over while the pointer is on it. With Motion set to Off nothing moves: the cards
/// are simply there and swap faces at once. Colours come from the palette.
/// </summary>
public sealed class StudioWallPanel : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<WallCard>?> CardsProperty =
        AvaloniaProperty.Register<StudioWallPanel, IReadOnlyList<WallCard>?>(nameof(Cards));

    private const double CardWidth = 250, CardHeight = 172, RiseSeconds = 0.45, Stagger = 0.045, TurnSeconds = 0.32;

    private readonly WrapPanel wall = new() { Margin = new Thickness(22, 20) };
    private readonly List<Face> faces = [];
    private TimeSpan? last;
    private bool asked;
    private double opened;

    public StudioWallPanel()
    {
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            // Never sideways: the cards wrap to the width there is.
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = wall,
        };
        ResourcesChanged += (_, _) => Build();
    }

    public IReadOnlyList<WallCard>? Cards { get => GetValue(CardsProperty); set => SetValue(CardsProperty, value); }

    // The page offers as much width as is asked for, and a wall asked that way would be one endless row. It asks
    // for a few cards' width and no more, and is then stretched to the width the page really has.
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? (CardWidth + 16) * 3 + 44 : availableSize.Width;
        var wanted = base.MeasureOverride(new Size(width, availableSize.Height));
        return new Size(Math.Min(wanted.Width, width), wanted.Height);
    }

    /// <summary>A click on a card: open its client.</summary>
    public event Action<WallCard>? Opened;

    /// <summary>Jumps the cards to where they end up, so a picture of the wall is the same each time.</summary>
    public void Settle()
    {
        opened = 1000;
        foreach (var face in faces)
            face.Place(opened, still: true);
    }

    /// <summary>Turns a card to its back or its front at once, for a picture of what the back looks like.</summary>
    public void Turn(int card, bool toBack)
    {
        if (card < 0 || card >= faces.Count)
            return;
        faces[card].Wanted = toBack;
        faces[card].Turned = toBack ? 1 : 0;
        faces[card].Place(opened, still: true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CardsProperty)
            Build();
        else if (change.Property == IsVisibleProperty)
            Ask();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Build();
        if (Spinner.Still)
            Settle();
        Ask();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        last = null;
        wall.Children.Clear();
        faces.Clear();
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private static Color ColourOf(IBrush brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;

    private void Build()
    {
        wall.Children.Clear();
        faces.Clear();
        if (TopLevel.GetTopLevel(this) is null)
            return;
        var ink = Brush("NavText");
        var soft = Brush("NavMuted");
        var ground = ColourOf(Brush("Nav"));
        var number = 0;
        foreach (var card in Cards ?? [])
        {
            var colour = ColourOf(Brush(card.Colour));
            var light = Brush(card.Client.State switch { StarState.Problem => "CategoryRed", StarState.Busy => "ProjectYellowOrange", _ => "ProjectAqua" });

            var front = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(18, 16) };
            var badge = new Border
            {
                Width = 58, Height = 58, CornerRadius = new CornerRadius(29), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(colour),
                Child = new TextBlock
                {
                    Text = card.Initials, FontSize = card.Initials.Length > 2 ? 16 : 21, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(ground),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            status.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = light, VerticalAlignment = VerticalAlignment.Center });
            status.Children.Add(new TextBlock { Text = card.Status, Foreground = soft }.Sized("Text12"));
            front.Children.Add(badge);
            front.Children.Add(status);
            var names = new StackPanel { Spacing = 2 };
            names.Children.Add(new TextBlock { Text = card.Client.Name, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis });
            names.Children.Add(new TextBlock { Text = card.Site.Length > 0 ? card.Site : card.Client.Detail, Foreground = soft, TextTrimming = TextTrimming.CharacterEllipsis }.Sized("Text12"));
            Grid.SetRow(names, 1);
            front.Children.Add(names);

            var back = new StackPanel { Spacing = 5, Margin = new Thickness(18, 14), IsVisible = false };
            back.Children.Add(new TextBlock { Text = card.Client.Name, FontWeight = FontWeight.SemiBold, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis });
            if (card.Busy.Count == 0)
                back.Children.Add(new TextBlock { Text = "Nothing on the go", Foreground = soft }.Sized("Text12"));
            foreach (var thing in card.Busy)
            {
                var row = new DockPanel();
                var dot = new Ellipse
                {
                    Width = 7, Height = 7, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
                    Fill = Brush(thing.State == StarState.Problem ? "CategoryRed" : thing.Kind switch
                    {
                        StarKind.Project => "ProjectYellowOrange", StarKind.Site => "ProjectAqua", StarKind.Quote => "ProjectHotPink", _ => "ProjectPurple",
                    }),
                };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                var detail = new TextBlock { Text = thing.Detail, Foreground = soft, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Sized("Text11");
                DockPanel.SetDock(detail, Dock.Right);
                row.Children.Add(detail);
                row.Children.Add(new TextBlock { Text = thing.Name, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis }.Sized("Text12"));
                back.Children.Add(row);
            }
            if (card.More > 0)
                back.Children.Add(new TextBlock { Text = $"and {card.More} more", Foreground = soft }.Sized("Text11"));

            var turn = new ScaleTransform(1, 1);
            var rise = new TranslateTransform();
            var body = new Border
            {
                Width = CardWidth, Height = CardHeight, Margin = new Thickness(8), CornerRadius = new CornerRadius(16), ClipToBounds = true,
                // The client's colour, low in one corner and fading out, over the dark ground.
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(105, colour.R, colour.G, colour.B), 0),
                        new GradientStop(Color.FromArgb(26, colour.R, colour.G, colour.B), 0.65),
                        new GradientStop(Color.FromArgb(16, colour.R, colour.G, colour.B), 1),
                    },
                },
                BorderBrush = new SolidColorBrush(colour, 0.55), BorderThickness = new Thickness(1),
                Child = new Panel { Children = { front, back } },
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = new TransformGroup { Children = { turn, rise } },
                Cursor = new Cursor(StandardCursorType.Hand),
                Opacity = 0,
            };
            var face = new Face(body, front, back, turn, rise, number++);
            body.PointerEntered += (_, _) => Want(face, true);
            body.PointerExited += (_, _) => Want(face, false);
            var shown = card;
            body.PointerPressed += (_, _) => Opened?.Invoke(shown);
            faces.Add(face);
            wall.Children.Add(body);
        }
        // Cards made after the wall has opened (the list changed) are simply there.
        foreach (var face in faces)
            face.Place(opened, Spinner.Still);
        Ask();
    }

    private void Want(Face face, bool back)
    {
        face.Wanted = back;
        if (Spinner.Still)
        {
            face.Turned = back ? 1 : 0;
            face.Place(opened, still: true);
            return;
        }
        Ask();
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
        var passed = last is { } before ? Math.Min(0.1, (now - before).TotalSeconds) : 0;
        last = now;
        opened += passed;
        var moving = false;
        foreach (var face in faces)
        {
            var target = face.Wanted ? 1.0 : 0.0;
            if (Math.Abs(face.Turned - target) > 0.0001)
            {
                face.Turned = target > face.Turned ? Math.Min(target, face.Turned + passed / TurnSeconds) : Math.Max(target, face.Turned - passed / TurnSeconds);
                moving = true;
            }
            moving |= face.Place(opened, still: false);
        }
        // Once every card is in place and none is turning, nothing more is asked for until the pointer moves one.
        if (moving && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not null)
            Ask();
        else
            last = null;
    }

    /// <summary>One card and how far it has risen and turned.</summary>
    private sealed class Face(Border body, Control front, Control back, ScaleTransform turn, TranslateTransform rise, int number)
    {
        public bool Wanted { get; set; }

        /// <summary>0 shows the front, 1 the back; halfway the card is edge-on and the faces swap.</summary>
        public double Turned { get; set; }

        /// <summary>Puts the card where it should be now. True while it is still rising.</summary>
        public bool Place(double opened, bool still)
        {
            var risen = still ? 1 : Math.Clamp((opened - number * Stagger) / RiseSeconds, 0, 1);
            var eased = 1 - Math.Pow(1 - risen, 3);
            body.Opacity = eased;
            rise.Y = (1 - eased) * 26;
            turn.ScaleX = Math.Max(0.02, Math.Abs(Math.Cos(Turned * Math.PI)));
            var showBack = Turned >= 0.5;
            if (back.IsVisible != showBack)
            {
                back.IsVisible = showBack;
                front.IsVisible = !showBack;
            }
            return risen < 1;
        }
    }
}
