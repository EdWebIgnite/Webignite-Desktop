using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Webignite.Core.Layout;

namespace Webignite.Desktop.Controls;

/// <summary>Which part of a widget a <see cref="TintedPart"/> is.</summary>
public enum TintPart
{
    Card,
    Head,
    Line,
}

/// <summary>
/// A part of one of the person's widgets that can wear the widget's colour: the card itself, the band behind its
/// heading, or the line along its top. The colour is never used at full strength on the card or the band: it is
/// mixed into the look's own Surface by the share <see cref="WidgetLooks.Shade"/> gives, so it follows light and
/// dark looks and the writing on it stays readable. With no colour the part is exactly as its style draws it.
/// </summary>
public sealed class TintedPart : Border
{
    public static readonly StyledProperty<string?> ColourProperty = AvaloniaProperty.Register<TintedPart, string?>(nameof(Colour));
    public static readonly StyledProperty<WidgetTint> TintProperty = AvaloniaProperty.Register<TintedPart, WidgetTint>(nameof(Tint));
    public static readonly StyledProperty<TintPart> PartProperty = AvaloniaProperty.Register<TintedPart, TintPart>(nameof(Part));

    protected override Type StyleKeyOverride => typeof(Border);

    public TintedPart()
    {
        // The look changing (light to dark, or another palette) changes what the colour is mixed into.
        ActualThemeVariantChanged += (_, _) => Apply();
        ResourcesChanged += (_, _) => Apply();
        // While arranging, the card's outline is the style's, to show which widgets can be moved.
        Classes.CollectionChanged += (_, _) => Apply();
    }

    public string? Colour { get => GetValue(ColourProperty); set => SetValue(ColourProperty, value); }
    public WidgetTint Tint { get => GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public TintPart Part { get => GetValue(PartProperty); set => SetValue(PartProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColourProperty || change.Property == TintProperty || change.Property == PartProperty)
            Apply();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Apply();
    }

    private void Apply()
    {
        var shade = WidgetLooks.Shade(Tint);
        var colour = Colour is null ? null : Found(CategoryDot.ResourceKey(Colour));
        var surface = Found("Surface");
        IBrush? Mixed(double share) => colour is { } c && surface is { } s && share > 0
            ? new SolidColorBrush(Color.FromRgb(WidgetLooks.Mix(s.R, c.R, share), WidgetLooks.Mix(s.G, c.G, share), WidgetLooks.Mix(s.B, c.B, share)))
            : null;
        switch (Part)
        {
            case TintPart.Card:
                Set(BackgroundProperty, Mixed(shade.Body));
                Set(BorderBrushProperty, Classes.Contains("arranging") ? null : Mixed(shade.Edge));
                break;
            case TintPart.Head:
                Set(BackgroundProperty, Mixed(shade.Head));
                // A band needs a little air under it before the widget's own content starts.
                Margin = Mixed(shade.Head) is null ? default : new Thickness(0, 0, 0, 8);
                break;
            default:
                IsVisible = colour is not null && shade.Line > 0;
                Set(BackgroundProperty, IsVisible ? Mixed(shade.Line) : null);
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

    private Color? Found(string key) => this.TryFindResource(key, ActualThemeVariant, out var found) && found is ISolidColorBrush brush ? brush.Color : null;
}
