using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Webignite.Core.Layout;

namespace Webignite.Desktop.Controls;

/// <summary>
/// The pop-up a tile's colours are picked from, for a widget on the person's own pages and for any card in the
/// app. The heading, the body and the line along the top are picked one at a time, each from the pastels, a colour
/// code typed in, or a colour clicked on the screen; one intensity goes for the heading and body together. It is
/// built when asked for and holds nothing once closed.
/// </summary>
public static class TilePicker
{
    private enum Part
    {
        Heading,
        Body,
        Line,
    }

    private const double Wide = 10 * 30 + 9 * 2;

    /// <param name="look">The tile's colours now; null when it is plain.</param>
    /// <param name="wear">Gives the tile new colours (null makes it plain) and keeps them.</param>
    /// <param name="close">Closes the pop-up.</param>
    /// <param name="fromScreen">Freezes the screen for the eyedropper and hands back the colour clicked; null where there is none.</param>
    /// <param name="hasHeading">False for a card with no heading of its own, which has no heading colour to pick.</param>
    public static Control Build(TileLook? look, Action<TileLook?> wear, Action close, Action<Action<string>>? fromScreen = null, bool hasHeading = true)
    {
        var host = new ContentControl();
        var now = look;
        var part = hasHeading ? Part.Heading : Part.Body;
        void Fill() => host.Content = Panel(now, part, hasHeading,
            picked =>
            {
                part = picked;
                Fill();
            },
            changed =>
            {
                now = TileLook.Tidied(changed);
                wear(now);
                Fill();
            },
            quietly =>
            {
                // The intensity as the slider moves: kept, without rebuilding the pop-up under the pointer.
                now = TileLook.Tidied(quietly);
                wear(now);
            },
            close, fromScreen);
        Fill();
        return host;
    }

    private static string? Of(TileLook? look, Part part) => part switch
    {
        Part.Heading => look?.Head,
        Part.Body => look?.Body,
        _ => look?.Line,
    };

    private static TileLook With(TileLook? look, Part part, string? colour)
    {
        var was = look ?? new TileLook();
        return part switch
        {
            Part.Heading => was with { Head = colour },
            Part.Body => was with { Body = colour },
            _ => was with { Line = colour },
        };
    }

    private static StackPanel Panel(TileLook? look, Part part, bool hasHeading, Action<Part> pick, Action<TileLook?> wear, Action<TileLook?> wearQuietly,
        Action close, Action<Action<string>>? fromScreen)
    {
        static TextBlock Label(string text)
        {
            var label = new TextBlock { Text = text };
            label.Classes.Add("label");
            label.Classes.Add("caps");
            return label;
        }
        var colour = Of(look, part);

        // Which part is being coloured: each button shows the colour that part has now.
        var parts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (which, name) in new[] { (Part.Heading, "Heading"), (Part.Body, "Body"), (Part.Line, "Top line") })
        {
            if (which == Part.Heading && !hasHeading)
                continue;
            var has = Of(look, which);
            var words = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { words } };
            if (has is not null)
                content.Children.Insert(0, new CategoryDot { Colour = has, Size = 10 });
            var button = new Button { Content = content };
            button.Classes.Add(which == part ? "primary" : "quiet");
            button.Click += (_, _) => pick(which);
            parts.Children.Add(button);
        }

        // The pastels: ten to a row, each a swatch of itself.
        var pastels = new WrapPanel { Width = Wide, ItemSpacing = 2, LineSpacing = 2 };
        foreach (var pastel in WidgetLooks.Pastels)
        {
            var swatch = new Button { Content = new CategoryDot { Colour = pastel.Hex, Size = 20 }, Padding = new Thickness(4), Width = 30, Height = 30 };
            swatch.Classes.Add(string.Equals(colour, pastel.Hex, StringComparison.OrdinalIgnoreCase) ? "primary" : "quiet");
            ToolTip.SetTip(swatch, pastel.Name);
            swatch.Click += (_, _) => wear(With(look, part, pastel.Hex));
            pastels.Children.Add(swatch);
        }

        // Any colour by its code, typed or picked off the screen.
        var code = new TextBox { Text = WidgetLooks.Code(colour) is null ? "" : colour, PlaceholderText = "#C6E3F7", Width = 110 };
        var wrong = new TextBlock { Text = "Type a colour code such as #C6E3F7.", IsVisible = false, TextWrapping = TextWrapping.Wrap, MaxWidth = Wide }.Sized("Text12");
        wrong.Classes.Add("short");
        void UseCode()
        {
            if (WidgetLooks.Known(code.Text) is { } known && WidgetLooks.Code(known) is not null)
                wear(With(look, part, known));
            else
                wrong.IsVisible = true;
        }
        code.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            UseCode();
            e.Handled = true;
        };
        var use = new Button { Content = "Use it" };
        use.Classes.Add("quiet");
        use.Click += (_, _) => UseCode();
        var own = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { code, use } };
        if (fromScreen is not null)
        {
            var screen = new Button { Content = "From the screen" };
            screen.Classes.Add("quiet");
            ToolTip.SetTip(screen, "Click any colour on the screen, such as one from a client's logo");
            screen.Click += (_, _) =>
            {
                close();
                fromScreen(picked => wear(With(look, part, picked)));
            };
            own.Children.Add(screen);
        }
        var plainPart = new Button { Content = part == Part.Line ? "No line" : $"Leave the {(part == Part.Heading ? "heading" : "body")} plain", IsEnabled = colour is not null, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        plainPart.Classes.Add("link");
        plainPart.Click += (_, _) => wear(With(look, part, null));

        // Intensity: how strongly the heading and body colours show, changed as the slider moves so the tile behind shows it.
        var strength = look?.Strength ?? WidgetLooks.UsualStrength;
        var percent = new TextBlock { Text = $"{strength}%", VerticalAlignment = VerticalAlignment.Center, Width = 44, TextAlignment = TextAlignment.Right };
        percent.Classes.Add("muted");
        var slider = new Slider
        {
            Minimum = WidgetLooks.LeastStrength, Maximum = WidgetLooks.MostStrength, Value = strength, Width = Wide - 54,
            TickFrequency = WidgetLooks.StrengthStep, IsSnapToTickEnabled = true, IsEnabled = look is { Head: not null } or { Body: not null },
        };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBaseValue || look is null)
                return;
            var moved = WidgetLooks.Strength((int)Math.Round(slider.Value));
            if (moved == look.Strength)
                return;
            look = look with { Strength = moved };
            percent.Text = $"{moved}%";
            wearQuietly(look);
        };

        var none = new Button { Content = "No colours at all", IsEnabled = look is not null, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0) };
        none.Classes.Add("link");
        none.Click += (_, _) =>
        {
            wear(null);
            close();
        };
        return new StackPanel
        {
            Spacing = 10, Margin = new Thickness(4),
            Children =
            {
                Label("Colour the"), parts, Label("Pastels"), pastels, Label("Your own colour"), own, wrong, plainPart,
                Label("Intensity"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { slider, percent } }, none,
            },
        };
    }

    private static AvaloniaProperty RangeBaseValue => Avalonia.Controls.Primitives.RangeBase.ValueProperty;
}
