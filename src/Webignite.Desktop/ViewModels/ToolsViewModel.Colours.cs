using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// Colour picker: type a colour or pick one off the screen, then copy it as hex, RGB or HSL.
public sealed partial class ToolsViewModel
{
    [ObservableProperty] public partial string ColourInput { get; set; } = "#E8590C";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasColour), nameof(ColourHex), nameof(ColourRgb), nameof(ColourHsl), nameof(ColourBrush),
        nameof(OnWhite), nameof(OnBlack))]
    public partial Rgb? Colour { get; set; }

    [ObservableProperty] public partial string ColourMessage { get; set; } = "";
    [ObservableProperty] public partial bool Picking { get; set; }

    public bool HasColour => Colour is not null;
    public string ColourHex => Colour?.Hex ?? "";
    public string ColourRgb => Colour?.Css ?? "";
    public string ColourHsl => Colour?.Hsl ?? "";
    public IBrush? ColourBrush => Colour is { } c ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : null;
    public string OnWhite => Colour is { } c ? Ratio("White", Colours.Contrast(c, new Rgb(255, 255, 255))) : "";
    public string OnBlack => Colour is { } c ? Ratio("Black", Colours.Contrast(c, new Rgb(0, 0, 0))) : "";
    public bool CanPickFromScreen => OperatingSystem.IsWindows();

    public ObservableCollection<ShadeRow> Shades { get; } = [];

    private static string Ratio(string text, double ratio) =>
        string.Create(CultureInfo.InvariantCulture, $"{text} text on it: {ratio:0.0} to 1, {Colours.Readability(ratio).ToLowerInvariant()}");

    partial void OnColourInputChanged(string value)
    {
        Colour = Colours.Parse(value);
        ColourMessage = Colour is null && value.Trim().Length > 0 ? "Type a colour like #E8590C or rgb(232, 89, 12)." : "";
    }

    partial void OnColourChanged(Rgb? value)
    {
        Shades.Clear();
        if (value is { } c)
            foreach (var shade in Colours.Shades(c))
                Shades.Add(new ShadeRow(shade));
    }

    /// <summary>A colour read off the screen while picking.</summary>
    public void ShowPicked(byte r, byte g, byte b) => ColourInput = new Rgb(r, g, b).Hex;

    [RelayCommand]
    private void UseShade(ShadeRow row) => ColourInput = row.Colour.Hex;

    [RelayCommand]
    private Task CopyHex(Avalonia.Controls.TopLevel? top) => Copy(top, ColourHex);

    [RelayCommand]
    private Task CopyRgb(Avalonia.Controls.TopLevel? top) => Copy(top, ColourRgb);

    [RelayCommand]
    private Task CopyHsl(Avalonia.Controls.TopLevel? top) => Copy(top, ColourHsl);

    private async Task Copy(Avalonia.Controls.TopLevel? top, string text)
    {
        if (text.Length == 0 || top?.Clipboard is not { } clipboard)
            return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
        ColourMessage = $"Copied {text}.";
    }
}

public sealed record ShadeRow(Rgb Colour)
{
    public IBrush Brush => new SolidColorBrush(Color.FromRgb(Colour.R, Colour.G, Colour.B));
    public string Hex => Colour.Hex;
}
