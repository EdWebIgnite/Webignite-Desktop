using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Webignite.Core.Tools;

/// <summary>A colour in a palette, with the name it goes by: "primary", "ink".</summary>
public sealed record Swatch(string Name, string Hex);

/// <summary>The palette being worked on, kept between sessions.</summary>
public sealed record PaletteSettings
{
    public IReadOnlyList<Swatch> Swatches { get; init; } = [];
}

/// <summary>A step in a colour's scale, as design systems number them: 50 the lightest, 500 the colour itself, 900 the darkest.</summary>
public readonly record struct ScaleStep(int Number, Rgb Colour);

/// <summary>
/// Building a palette: a colour's lighter and darker steps, the colours that go with it, CMYK for print, whether
/// text can be read on a background, the main colours of a picture or an SVG, and the palette as CSS.
/// </summary>
public static partial class ColourPalettes
{
    /// <summary>The most colours a palette holds.</summary>
    public const int Most = 12;

    private static readonly (int Number, double White, double Black)[] Steps =
    [
        (50, 0.92, 0), (100, 0.82, 0), (200, 0.64, 0), (300, 0.44, 0), (400, 0.22, 0), (500, 0, 0), (600, 0, 0.16), (700, 0, 0.32), (800, 0, 0.48), (900, 0, 0.64),
    ];

    /// <summary>Tints and shades: the colour mixed with white up to 50 and with black down to 900. 500 is the colour itself.</summary>
    public static IReadOnlyList<ScaleStep> Scale(Rgb colour) =>
        [.. Steps.Select(step => new ScaleStep(step.Number, step.White > 0 ? Mix(colour, new Rgb(255, 255, 255), step.White) : Mix(colour, new Rgb(0, 0, 0), step.Black)))];

    /// <summary>The colour with some of another stirred in: 0 leaves it, 1 replaces it.</summary>
    public static Rgb Mix(Rgb colour, Rgb other, double amount) => new(
        Channel(colour.R + (other.R - colour.R) * amount), Channel(colour.G + (other.G - colour.G) * amount), Channel(colour.B + (other.B - colour.B) * amount));

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    /// <summary>Colours that sit well with this one, by where they are round the colour wheel.</summary>
    public static IReadOnlyList<(string Name, Rgb Colour)> GoesWith(Rgb colour)
    {
        var (h, s, l) = Colours.ToHsl(colour);
        Rgb Turned(double by) => Colours.FromHsl((h + by + 360) % 360, s, l);
        return
        [
            ("Opposite", Turned(180)), ("Next to it", Turned(-30)), ("Next to it", Turned(30)), ("Third", Turned(120)), ("Third", Turned(240)),
        ];
    }

    /// <summary>
    /// The colour for print, as percentages of cyan, magenta, yellow and black. A plain conversion: a printer's
    /// own profile will shift it a little, so it is a starting point, not a proof.
    /// </summary>
    public static (int C, int M, int Y, int K) ToCmyk(Rgb colour)
    {
        double r = colour.R / 255d, g = colour.G / 255d, b = colour.B / 255d;
        var k = 1 - Math.Max(r, Math.Max(g, b));
        if (k >= 1)
            return (0, 0, 0, 100);
        static int Percent(double value) => (int)Math.Round(value * 100);
        return (Percent((1 - r - k) / (1 - k)), Percent((1 - g - k) / (1 - k)), Percent((1 - b - k) / (1 - k)), Percent(k));
    }

    public static string Cmyk(Rgb colour)
    {
        var (c, m, y, k) = ToCmyk(colour);
        return string.Create(CultureInfo.InvariantCulture, $"cmyk({c}%, {m}%, {y}%, {k}%)");
    }

    /// <summary>What the accessibility rules (WCAG) say about text of one colour on another.</summary>
    public static IReadOnlyList<(string What, bool Passes)> Verdicts(double ratio) =>
    [
        ("Normal text (needs 4.5 to 1)", ratio >= 4.5),
        ("Large headings, 24 px and up (needs 3 to 1)", ratio >= 3),
        ("Icons and the edges of buttons and boxes (needs 3 to 1)", ratio >= 3),
        ("Normal text, the stricter level (needs 7 to 1)", ratio >= 7),
    ];

    public static string Ratio(double ratio) => string.Create(CultureInfo.InvariantCulture, $"{ratio:0.0} to 1");

    /// <summary>
    /// The main colours of a picture, the most used first. Pixels are 0xAARRGGBB; see-through ones are skipped.
    /// Colours that are nearly the same are counted as one, so a photo gives a handful of distinct colours and
    /// not six shades of sky.
    /// </summary>
    public static IReadOnlyList<Rgb> FromPixels(ReadOnlySpan<uint> pixels, int count = 6)
    {
        // Each colour is dropped into one of 4 096 bins (16 steps of red, green and blue), keeping a running sum
        // so a bin answers with the average of what fell into it.
        var bins = new Dictionary<int, (long R, long G, long B, int N)>();
        foreach (var pixel in pixels)
        {
            if (pixel >> 24 < 128)
                continue;
            var (r, g, b) = ((int)(pixel >> 16 & 0xFF), (int)(pixel >> 8 & 0xFF), (int)(pixel & 0xFF));
            var key = r >> 4 << 8 | g >> 4 << 4 | b >> 4;
            bins.TryGetValue(key, out var bin);
            bins[key] = (bin.R + r, bin.G + g, bin.B + b, bin.N + 1);
        }
        var found = new List<(Rgb Colour, int N)>();
        foreach (var bin in bins.Values.OrderByDescending(b => b.N))
        {
            var colour = new Rgb((byte)(bin.R / bin.N), (byte)(bin.G / bin.N), (byte)(bin.B / bin.N));
            var near = found.FindIndex(f => Distance(f.Colour, colour) < 48);
            if (near >= 0)
                found[near] = (found[near].Colour, found[near].N + bin.N);
            else
                found.Add((colour, bin.N));
        }
        return [.. found.OrderByDescending(f => f.N).Take(count).Select(f => f.Colour)];
    }

    private static double Distance(Rgb a, Rgb b) => Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

    [GeneratedRegex(@"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b|rgb\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*\)")]
    private static partial Regex ColourCode();

    /// <summary>The colours written in an SVG (a logo's fills and strokes), in the order they first appear, each once.</summary>
    public static IReadOnlyList<Rgb> FromSvg(string svg, int count = Most)
    {
        var found = new List<Rgb>();
        foreach (Match match in ColourCode().Matches(svg))
        {
            if (Colours.Parse(match.Value) is { } colour && !found.Contains(colour))
                found.Add(colour);
            if (found.Count == count)
                break;
        }
        return found;
    }

    /// <summary>The palette with a colour added at the end, named "colour-3" unless given a name. A colour already in it, or a full palette, changes nothing.</summary>
    public static IReadOnlyList<Swatch> Add(IReadOnlyList<Swatch> palette, Rgb colour, string? name = null)
    {
        if (palette.Count >= Most || palette.Any(s => string.Equals(s.Hex, colour.Hex, StringComparison.OrdinalIgnoreCase)))
            return palette;
        var number = palette.Count + 1;
        var given = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        while (given is null && palette.Any(s => s.Name == $"colour-{number}"))
            number++;
        return [.. palette, new Swatch(given ?? $"colour-{number}", colour.Hex)];
    }

    /// <summary>A name as CSS allows it: lower case, dashes for anything else. "Brand Orange!" is "brand-orange".</summary>
    public static string CssName(string name)
    {
        var text = new StringBuilder();
        foreach (var c in name.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c))
                text.Append(char.ToLowerInvariant(c));
            else if (text.Length > 0 && text[^1] != '-')
                text.Append('-');
        }
        var tidy = text.ToString().Trim('-');
        return tidy.Length == 0 ? "colour" : tidy;
    }

    /// <summary>The palette as CSS variables, ready to paste into a stylesheet. Two colours with one name get a number.</summary>
    public static string CssVariables(IReadOnlyList<Swatch> palette)
    {
        var css = new StringBuilder(":root {\n");
        var used = new HashSet<string>();
        foreach (var swatch in palette)
        {
            var name = CssName(swatch.Name);
            for (var n = 2; !used.Add(name); n++)
                name = $"{CssName(swatch.Name)}-{n}";
            css.Append(CultureInfo.InvariantCulture, $"  --{name}: {swatch.Hex.ToUpperInvariant()};\n");
        }
        return css.Append('}').ToString();
    }

    /// <summary>The palette as plain lines, "primary #E8590C", for a brief or a mail.</summary>
    public static string Listed(IReadOnlyList<Swatch> palette) => string.Join(Environment.NewLine, palette.Select(s => $"{s.Name} {s.Hex.ToUpperInvariant()}"));
}
