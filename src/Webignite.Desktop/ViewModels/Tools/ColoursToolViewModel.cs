using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// Colour picker: type a colour or pick one off the screen, then copy it as hex, RGB or HSL.
public sealed partial class ColoursToolViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices services;

    public ColoursToolViewModel(AppServices services)
    {
        this.services = services;
        OnColourInputChanged(ColourInput);
    }

    /// <summary>The tool's window closed: what it started is stopped and what it held is freed.</summary>
    public void Dispose()
    {
        StopColours();
    }

    [ObservableProperty] public partial string ColourInput { get; set; } = "#E8590C";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasColour), nameof(ColourHex), nameof(ColourRgb), nameof(ColourHsl), nameof(ColourCmyk), nameof(ColourBrush),
        nameof(OnWhite), nameof(OnBlack))]
    public partial Rgb? Colour { get; set; }

    [ObservableProperty] public partial string ColourMessage { get; set; } = "";

    public bool HasColour => Colour is not null;
    public string ColourHex => Colour?.Hex ?? "";
    public string ColourRgb => Colour?.Css ?? "";
    public string ColourHsl => Colour?.Hsl ?? "";
    public string ColourCmyk => Colour is { } c ? ColourPalettes.Cmyk(c) : "";
    public IBrush? ColourBrush => Colour is { } c ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : null;
    public string OnWhite => Colour is { } c ? Ratio("White", Colours.Contrast(c, new Rgb(255, 255, 255))) : "";
    public string OnBlack => Colour is { } c ? Ratio("Black", Colours.Contrast(c, new Rgb(0, 0, 0))) : "";
    public bool CanPickFromScreen => OperatingSystem.IsWindows();

    public ObservableCollection<ShadeRow> Shades { get; } = [];

    /// <summary>Colours picked or copied lately, newest first, to pick up again with a click.</summary>
    public ObservableCollection<ShadeRow> RecentColours { get; } = [];

    public bool HasRecentColours => RecentColours.Count > 0;

    /// <summary>Starts following the recent colours; the view calls it when shown and <see cref="StopColours"/> when closed.</summary>
    public void WatchColours()
    {
        services.ColoursChanged += LoadRecentColours;
        LoadRecentColours();
    }

    public void StopColours() => services.ColoursChanged -= LoadRecentColours;

    private void LoadRecentColours()
    {
        RecentColours.Clear();
        foreach (var hex in services.Settings.RecentColours)
            if (Colours.Parse(hex) is { } colour)
                RecentColours.Add(new ShadeRow(colour));
        OnPropertyChanged(nameof(HasRecentColours));
    }

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
        GoesWith.Clear();
        if (value is { } c)
        {
            // The scale design systems use: 50 the lightest, 500 the colour itself, 900 the darkest.
            foreach (var step in ColourPalettes.Scale(c))
                Shades.Add(new ShadeRow(step.Colour, step.Number.ToString(CultureInfo.InvariantCulture)));
            foreach (var (name, colour) in ColourPalettes.GoesWith(c))
                GoesWith.Add(new ShadeRow(colour, name));
        }
        // Whatever colour is showing is the text colour in Contrast until another is typed there.
        if (value is { } shown && !contrastTextTyped)
            ContrastText = shown.Hex;
    }

    /// <summary>Colours that sit well with the one showing: its opposite, its neighbours and its thirds round the wheel.</summary>
    public ObservableCollection<ShadeRow> GoesWith { get; } = [];

    [RelayCommand]
    private Task CopyCmyk(Avalonia.Controls.TopLevel? top) => Copy(top, ColourCmyk);

    // The three parts of the tool

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsColourPart), nameof(IsPalettePart), nameof(IsContrastPart))]
    public partial ColourPart ColourPart { get; set; } = ColourPart.Colour;

    public bool IsColourPart => ColourPart == ColourPart.Colour;
    public bool IsPalettePart => ColourPart == ColourPart.Palette;
    public bool IsContrastPart => ColourPart == ColourPart.Contrast;

    [RelayCommand]
    private void ShowColourPart(ColourPart part)
    {
        ColourPart = part;
        if (part == ColourPart.Palette)
            LoadPalette();
    }

    // Palette: a handful of named colours, kept between sessions, copied out as CSS variables.

    public ObservableCollection<SwatchRow> PaletteRows { get; } = [];

    public bool HasPalette => PaletteRows.Count > 0;

    [ObservableProperty] public partial string PaletteMessage { get; set; } = "";

    public void LoadPalette()
    {
        PaletteRows.Clear();
        foreach (var swatch in services.Settings.Palette.Swatches)
            if (Colours.Parse(swatch.Hex) is { } colour)
                PaletteRows.Add(new SwatchRow(swatch.Name, colour, this));
        OnPropertyChanged(nameof(HasPalette));
    }

    /// <summary>The palette as it stands on screen, names as typed. Saved after every change.</summary>
    internal void SavePalette() =>
        services.Settings.Palette = new PaletteSettings { Swatches = [.. PaletteRows.Select(r => new Swatch(r.Name, r.Hex))] };

    private IReadOnlyList<Swatch> PaletteNow => [.. PaletteRows.Select(r => new Swatch(r.Name, r.Hex))];

    private void PutPalette(IReadOnlyList<Swatch> swatches)
    {
        services.Settings.Palette = new PaletteSettings { Swatches = swatches };
        LoadPalette();
    }

    [RelayCommand]
    private void AddToPalette()
    {
        if (Colour is not { } colour)
            return;
        var before = PaletteNow;
        var after = ColourPalettes.Add(before, colour);
        PutPalette(after);
        ColourMessage = after.Count > before.Count ? $"{colour.Hex} is in the palette." : before.Count >= ColourPalettes.Most ? $"The palette is full at {ColourPalettes.Most} colours." : "That colour is in the palette already.";
    }

    /// <summary>Adds the main colours of a picture, or the colours written in an SVG. Gives how many were new.</summary>
    public void AddToPalette(IReadOnlyList<Rgb> colours, string from)
    {
        var before = PaletteNow;
        var after = before;
        foreach (var colour in colours)
            after = ColourPalettes.Add(after, colour);
        PutPalette(after);
        var added = after.Count - before.Count;
        PaletteMessage = colours.Count == 0 ? $"No colours could be read from {from}."
            : added == 0 ? "Those colours are in the palette already, or it is full."
            : (added == 1 ? "1 colour" : $"{added} colours") + $" added from {from}. Name them, and take out any that don't belong.";
    }

    /// <summary>The colours of a picture file: written in it for an SVG, the most used for anything else.</summary>
    public static IReadOnlyList<Rgb> PictureColours(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
            return ColourPalettes.FromSvg(File.ReadAllText(path), 6);
        // A small copy is plenty to count colours in, and quick.
        var (pixels, _, _, _, _) = Webignite.Tools.PictureTools.ForViewing(path, 160);
        return ColourPalettes.FromPixels(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixels));
    }

    internal void RemoveFromPalette(SwatchRow row)
    {
        PaletteRows.Remove(row);
        SavePalette();
        OnPropertyChanged(nameof(HasPalette));
        PaletteMessage = "";
    }

    [RelayCommand]
    private void ClearPalette()
    {
        PutPalette([]);
        PaletteMessage = "";
    }

    [RelayCommand]
    private async Task CopyPaletteCss(Avalonia.Controls.TopLevel? top)
    {
        await ToolClipboard.CopyAsync(top, ColourPalettes.CssVariables(PaletteNow));
        PaletteMessage = "Copied as CSS variables. Paste them at the top of the stylesheet.";
    }

    [RelayCommand]
    private async Task CopyPaletteList(Avalonia.Controls.TopLevel? top)
    {
        await ToolClipboard.CopyAsync(top, ColourPalettes.Listed(PaletteNow));
        PaletteMessage = "Copied as a list of names and codes.";
    }

    // Contrast: whether text of one colour can be read on another.

    private bool contrastTextTyped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContrast), nameof(ContrastRatio), nameof(ContrastTextBrush), nameof(ContrastBackBrush))]
    public partial string ContrastText { get; set; } = "#1C1D21";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContrast), nameof(ContrastRatio), nameof(ContrastTextBrush), nameof(ContrastBackBrush))]
    public partial string ContrastBack { get; set; } = "#FFFFFF";

    public bool HasContrast => Colours.Parse(ContrastText) is not null && Colours.Parse(ContrastBack) is not null;

    public string ContrastRatio => Colours.Parse(ContrastText) is { } text && Colours.Parse(ContrastBack) is { } back
        ? ColourPalettes.Ratio(Colours.Contrast(text, back)) : "Type two colours, like #1C1D21 and #FFFFFF.";

    public IBrush? ContrastTextBrush => Colours.Parse(ContrastText) is { } c ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : null;
    public IBrush? ContrastBackBrush => Colours.Parse(ContrastBack) is { } c ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : null;

    public ObservableCollection<VerdictRow> ContrastVerdicts { get; } = [];

    /// <summary>Called by the view when the text colour box is typed in, so it stops following the Colour part.</summary>
    public void ContrastTextWasTyped() => contrastTextTyped = true;

    partial void OnContrastTextChanged(string value) => JudgeContrast();

    partial void OnContrastBackChanged(string value) => JudgeContrast();

    private void JudgeContrast()
    {
        ContrastVerdicts.Clear();
        if (Colours.Parse(ContrastText) is not { } text || Colours.Parse(ContrastBack) is not { } back)
            return;
        foreach (var (what, passes) in ColourPalettes.Verdicts(Colours.Contrast(text, back)))
            ContrastVerdicts.Add(new VerdictRow(what, passes));
    }

    [RelayCommand]
    private void SwapContrast()
    {
        contrastTextTyped = true;
        (ContrastText, ContrastBack) = (ContrastBack, ContrastText);
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
        if (Colour is { } copied)
            services.RememberColour(copied.Hex);
    }
}

public enum ColourPart
{
    Colour,
    Palette,
    Contrast,
}

public sealed record ShadeRow(Rgb Colour, string Label = "")
{
    public IBrush Brush => new SolidColorBrush(Color.FromRgb(Colour.R, Colour.G, Colour.B));
    public string Hex => Colour.Hex;

    /// <summary>"500 #E8590C" for a step in the scale, "Opposite #0C9BE8" for a colour that goes with it.</summary>
    public string Tip => Label.Length > 0 ? $"{Label} {Colour.Hex}" : Colour.Hex;
}

/// <summary>A named colour in the palette. Its name is saved as it is typed.</summary>
public sealed partial class SwatchRow(string name, Rgb colour, ColoursToolViewModel tools) : ObservableObject
{
    public Rgb Colour { get; } = colour;
    public string Hex => Colour.Hex;
    public IBrush Brush => new SolidColorBrush(Color.FromRgb(Colour.R, Colour.G, Colour.B));

    [ObservableProperty] public partial string Name { get; set; } = name;

    partial void OnNameChanged(string value) => tools.SavePalette();

    [RelayCommand]
    private void Remove() => tools.RemoveFromPalette(this);

    [RelayCommand]
    private void Use()
    {
        tools.ColourInput = Hex;
        tools.ShowColourPartCommand.Execute(ColourPart.Colour);
    }
}

/// <summary>One of the accessibility rules, and whether the two colours meet it.</summary>
public sealed record VerdictRow(string What, bool Passes)
{
    public string Mark => Passes ? "Yes" : "No";
}
