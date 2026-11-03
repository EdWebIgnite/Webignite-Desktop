using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A round badge with someone's initials on a colour picked from their name, like the contact badges in Apple Mail.
/// The rule for the letters and the colour is Core's <see cref="Core.Clients.Monogram"/>; the colour itself comes
/// from the palette's category set (CategoryBlue and so on), so it follows light and dark mode.
/// </summary>
public sealed class Monogram : Border
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<Monogram, string?>(nameof(Text));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<Monogram, double>(nameof(Size), 32);

    private readonly TextBlock letters = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeight.SemiBold,
    };

    private IDisposable? background;

    protected override Type StyleKeyOverride => typeof(Border);

    public Monogram()
    {
        VerticalAlignment = VerticalAlignment.Center;
        Child = letters;
        letters.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("NavText"));
        Apply();
    }

    /// <summary>The client's or sender's name, or an email address when that is all there is.</summary>
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == SizeProperty)
            Apply();
    }

    private void Apply()
    {
        Width = Height = Size;
        CornerRadius = new CornerRadius(Size / 2);
        letters.FontSize = Math.Round(Size * 0.4);
        letters.Text = Core.Clients.Monogram.Initials(Text);
        var colour = Core.Clients.Monogram.Colour(Text);
        background?.Dispose();
        background = Bind(BackgroundProperty, this.GetResourceObservable("Category" + char.ToUpperInvariant(colour[0]) + colour[1..]));
    }
}
