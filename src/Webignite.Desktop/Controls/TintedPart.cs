using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Webignite.Core.Layout;
using Webignite.Core.Tools;

namespace Webignite.Desktop.Controls;

/// <summary>Which part of a widget a <see cref="TintedPart"/> is.</summary>
public enum TintPart
{
    Card,
    Head,
    Line,
}

/// <summary>
/// A part of one of the person's widgets that wears the widget's colours: the card itself (its body colour), the
/// band behind its heading, or the line along its top. Each colour is mixed into the look's own Surface as strongly
/// as the intensity says and no further than the writing on it can still be read (<see cref="TileBrushes"/>). With
/// no colour the part is exactly as its style draws it.
/// </summary>
public sealed class TintedPart : Border
{
    public static readonly StyledProperty<TileLook?> LookProperty = AvaloniaProperty.Register<TintedPart, TileLook?>(nameof(Look));
    public static readonly StyledProperty<TintPart> PartProperty = AvaloniaProperty.Register<TintedPart, TintPart>(nameof(Part));

    protected override Type StyleKeyOverride => typeof(Border);

    public TintedPart()
    {
        // The look changing (light to dark, or another palette) changes what the colours are mixed into.
        ActualThemeVariantChanged += (_, _) => Apply();
        ResourcesChanged += (_, _) => Apply();
        // While arranging, the card's outline is the style's, to show which widgets can be moved.
        Classes.CollectionChanged += (_, _) => Apply();
    }

    public TileLook? Look { get => GetValue(LookProperty); set => SetValue(LookProperty, value); }
    public TintPart Part { get => GetValue(PartProperty); set => SetValue(PartProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LookProperty || change.Property == PartProperty)
            Apply();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Apply();
    }

    private void Apply()
    {
        var brushes = new TileBrushes(this, Look);
        switch (Part)
        {
            case TintPart.Card:
                Set(BackgroundProperty, brushes.Body);
                Set(BorderBrushProperty, Classes.Contains("arranging") ? null : brushes.Edge);
                break;
            case TintPart.Head:
                Set(BackgroundProperty, brushes.Head);
                // A band needs a little air under it before the widget's own content starts.
                Margin = brushes.Head is null ? default : new Thickness(0, 0, 0, 8);
                break;
            default:
                IsVisible = brushes.Line is not null;
                Set(BackgroundProperty, brushes.Line);
                break;
        }
    }

    // A brush of ours, or with none the property goes back to whatever the style gives it.
    private void Set(StyledProperty<IBrush?> property, IBrush? brush)
    {
        if (brush is null)
            ClearValue(property);
        else
            SetValue(property, brush);
    }
}

/// <summary>
/// A tile's colours as they are drawn in the look on screen. A heading or body colour is mixed into the look's
/// Surface by the share its intensity gives, then held back to where even the faint (Muted) writing keeps its
/// contrast, so pastels are used nearly in full on a light look and become deep tints on a dark one. The line along
/// the top carries no writing and is the colour itself.
/// </summary>
public readonly struct TileBrushes
{
    public TileBrushes(StyledElement on, TileLook? look)
    {
        if (look is null || look.IsPlain || Found(on, "Surface") is not { } surface)
            return;
        var writing = Found(on, "Muted") ?? Found(on, "Text");
        var share = WidgetLooks.Share(look.Strength);
        Color? Mixed(string? colour, double wanted, bool readOn)
        {
            if (colour is null || (WidgetLooks.Code(colour) ?? Found(on, CategoryDot.ResourceKey(colour))) is not { } c)
                return null;
            if (readOn && writing is { } w)
                wanted = WidgetLooks.Readable(surface, c, w, wanted);
            var mixed = WidgetLooks.Mix(surface, c, wanted);
            return Color.FromRgb(mixed.R, mixed.G, mixed.B);
        }
        HeadColour = Mixed(look.Head, share, readOn: true);
        BodyColour = Mixed(look.Body, share, readOn: true);
        LineColour = Mixed(look.Line, 1, readOn: false);
        EdgeColour = Mixed(look.Head ?? look.Body, 0.8 * share, readOn: false);
        SurfaceColour = Color.FromRgb(surface.R, surface.G, surface.B);
    }

    public Color? HeadColour { get; }
    public Color? BodyColour { get; }
    public Color? LineColour { get; }
    public Color? EdgeColour { get; }

    /// <summary>The look's own surface, for the parts of a tile left plain.</summary>
    public Color SurfaceColour { get; }

    public IBrush? Head => HeadColour is { } c ? new SolidColorBrush(c) : null;
    public IBrush? Body => BodyColour is { } c ? new SolidColorBrush(c) : null;
    public IBrush? Line => LineColour is { } c ? new SolidColorBrush(c) : null;
    public IBrush? Edge => EdgeColour is { } c ? new SolidColorBrush(c) : null;

    private static Rgb? Found(StyledElement on, string key) =>
        on.TryFindResource(key, on.ActualThemeVariant, out var found) && found is ISolidColorBrush brush ? new Rgb(brush.Color.R, brush.Color.G, brush.Color.B) : null;
}
