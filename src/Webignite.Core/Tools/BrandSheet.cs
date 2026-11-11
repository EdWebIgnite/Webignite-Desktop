namespace Webignite.Core.Tools;

/// <summary>What goes on a brand sheet: whose brand, its logo, its colours and its two typefaces.</summary>
public sealed record BrandSheetContent
{
    public string Name { get; init; } = "";

    /// <summary>The master logo's file, or empty for a sheet without one.</summary>
    public string LogoPath { get; init; } = "";

    public IReadOnlyList<Swatch> Colours { get; init; } = [];

    /// <summary>The typeface for headings, by its name as installed: "Montserrat".</summary>
    public string HeadingFont { get; init; } = "";

    /// <summary>The typeface for ordinary text.</summary>
    public string BodyFont { get; init; } = "";

    /// <summary>Who made the sheet, for the line at the foot. Empty leaves the line out.</summary>
    public string PreparedBy { get; init; } = "";
}

/// <summary>
/// The brand sheet: one A4 page a client can hand to a printer or a new designer, with the logo, each colour
/// under its name with its codes for screen and print, and the two typefaces shown in use.
/// </summary>
public static class BrandSheet
{
    /// <summary>The most colours that fit on the page with their codes readable.</summary>
    public const int MostColours = 8;

    public static string FileName(string? name) => (string.IsNullOrWhiteSpace(name) ? "Brand" : name.Trim()) + " brand sheet.pdf";

    /// <summary>Why the sheet can't be made yet, or null when it can.</summary>
    public static string? Problem(BrandSheetContent content)
    {
        if (string.IsNullOrWhiteSpace(content.Name))
            return "Say whose brand it is: the name goes at the top of the sheet.";
        if (content.LogoPath.Length == 0 && content.Colours.Count == 0 && content.HeadingFont.Trim().Length == 0 && content.BodyFont.Trim().Length == 0)
            return "There is nothing to put on the sheet yet. Choose a logo, build a palette in Colours, or name the typefaces.";
        return null;
    }

    /// <summary>The colours that go on the sheet: the first eight of the palette that are real colour codes.</summary>
    public static IReadOnlyList<(Swatch Swatch, Rgb Colour)> Shown(IReadOnlyList<Swatch> palette) =>
        [.. palette.Select(s => (Swatch: s, Colour: Colours.Parse(s.Hex))).Where(s => s.Colour is not null).Take(MostColours).Select(s => (s.Swatch, s.Colour!.Value))];

    /// <summary>The lines under a colour: its code for the web, for screens and for print.</summary>
    public static IReadOnlyList<string> Codes(Rgb colour) =>
        [colour.Hex, $"RGB {colour.R}, {colour.G}, {colour.B}", "CMYK " + ColourPalettes.Cmyk(colour)[5..^1].Replace("%", "", StringComparison.Ordinal)];

    /// <summary>True when words on this colour should be white: dark colours get white lettering, light ones dark.</summary>
    public static bool WhiteOn(Rgb colour) => Colours.Contrast(colour, new Rgb(255, 255, 255)) >= Colours.Contrast(colour, new Rgb(28, 29, 33));

    public const string HeadingSample = "Aa Bb Cc Dd Ee Ff Gg 0123456789";

    public const string BodySample = "The quick brown fox jumps over the lazy dog. Body text is set in this typeface: paragraphs, captions, buttons and forms.";

    /// <summary>What to say about typefaces the PC doesn't have, as their samples are then drawn in a stand-in.</summary>
    public static string? MissingFonts(IReadOnlyList<string> missing) => missing.Count switch
    {
        0 => null,
        1 => $"{missing[0]} isn't installed on this PC, so its sample is drawn in a stand-in. Install the font and save the sheet again.",
        _ => $"{string.Join(" and ", missing)} aren't installed on this PC, so their samples are drawn in a stand-in. Install the fonts and save the sheet again.",
    };
}
