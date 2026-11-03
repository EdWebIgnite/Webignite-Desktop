using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A small round dot in a category colour, like the colour dots in Apple Mail. The colour is a name from
/// MailCategories.Colours ("blue", "purple" and so on), looked up in the palette as CategoryBlue and so on, so
/// it follows light and dark mode.
/// </summary>
public sealed class CategoryDot : Border
{
    public static readonly StyledProperty<string?> ColourProperty = AvaloniaProperty.Register<CategoryDot, string?>(nameof(Colour));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<CategoryDot, double>(nameof(Size), 8);

    private IDisposable? binding;

    protected override Type StyleKeyOverride => typeof(Border);

    public CategoryDot()
    {
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Apply();
    }

    public string? Colour { get => GetValue(ColourProperty); set => SetValue(ColourProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>The palette name for a category colour: "blue" is CategoryBlue.</summary>
    public static string ResourceKey(string? colour) =>
        string.IsNullOrEmpty(colour) ? "CategoryGrey" : "Category" + char.ToUpperInvariant(colour[0]) + colour[1..];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColourProperty || change.Property == SizeProperty)
            Apply();
    }

    private void Apply()
    {
        Width = Height = Size;
        CornerRadius = new CornerRadius(Size / 2);
        binding?.Dispose();
        binding = Bind(BackgroundProperty, this.GetResourceObservable(ResourceKey(Colour)));
    }
}
