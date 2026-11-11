using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Webignite.Core.Tools;

/// <summary>What the logo is drawn in: its own colours, or all white (for dark backgrounds) or all black (for one-colour print).</summary>
public enum LogoInk
{
    Colour,
    White,
    Black,
}

/// <summary>What is behind the logo: nothing (see-through), white or black.</summary>
public enum LogoBack
{
    Transparent,
    White,
    Black,
}

/// <summary>One file in a logo pack: where it goes in the pack and how it is made.</summary>
public sealed record LogoFile(string Path, LogoInk Ink, LogoBack Back, int Width, string Format);

/// <summary>
/// The logo export pack: from one master logo, every version a client is likely to be asked for, named so they
/// can tell which is which, in folders by colour, with a note on what to use where.
/// </summary>
public static class LogoPack
{
    /// <summary>The widths made, in pixels: print and large banners, websites, mail signatures, thumbnails.</summary>
    public static IReadOnlyList<int> Widths { get; } = [2000, 1000, 500, 250];

    /// <summary>A name for files: "Karoo Coffee Roasters" is "karoo-coffee-roasters".</summary>
    public static string Stem(string? name)
    {
        var text = new StringBuilder();
        foreach (var c in (name ?? "").Trim())
        {
            if (char.IsAsciiLetterOrDigit(c))
                text.Append(char.ToLowerInvariant(c));
            else if (text.Length > 0 && text[^1] != '-')
                text.Append('-');
        }
        var tidy = text.ToString().Trim('-');
        return tidy.Length == 0 ? "logo" : tidy;
    }

    public static string ZipName(string? name) => (string.IsNullOrWhiteSpace(name) ? "Logo" : name.Trim()) + " logo pack.zip";

    /// <summary>The widths a master can be made at. A picture is never enlarged; a vector can be any size.</summary>
    public static IReadOnlyList<int> WidthsFor(int masterWidth, bool vector)
    {
        if (vector)
            return Widths;
        var fits = Widths.Where(w => w <= masterWidth).ToList();
        return fits.Count > 0 ? fits : [Math.Max(1, masterWidth)];
    }

    /// <summary>
    /// Every file in the pack. The all-white and all-black versions are only made when the master has a
    /// see-through background: without one they would be plain rectangles.
    /// </summary>
    public static IReadOnlyList<LogoFile> Plan(string? name, int masterWidth, bool vector, bool seeThrough)
    {
        var stem = Stem(name);
        var files = new List<LogoFile>();
        foreach (var width in WidthsFor(masterWidth, vector))
        {
            var px = width.ToString(CultureInfo.InvariantCulture) + "px";
            if (seeThrough)
            {
                files.Add(new LogoFile($"Colour/{stem}-{px}.png", LogoInk.Colour, LogoBack.Transparent, width, "png"));
                files.Add(new LogoFile($"Colour/{stem}-{px}.webp", LogoInk.Colour, LogoBack.Transparent, width, "webp"));
            }
            files.Add(new LogoFile($"Colour/{stem}-on-white-{px}.jpg", LogoInk.Colour, LogoBack.White, width, "jpg"));
            if (!seeThrough)
            {
                files.Add(new LogoFile($"Colour/{stem}-on-white-{px}.png", LogoInk.Colour, LogoBack.White, width, "png"));
                continue;
            }
            files.Add(new LogoFile($"White/{stem}-white-{px}.png", LogoInk.White, LogoBack.Transparent, width, "png"));
            files.Add(new LogoFile($"White/{stem}-white-on-black-{px}.jpg", LogoInk.White, LogoBack.Black, width, "jpg"));
            files.Add(new LogoFile($"Black/{stem}-black-{px}.png", LogoInk.Black, LogoBack.Transparent, width, "png"));
            files.Add(new LogoFile($"Black/{stem}-black-on-white-{px}.jpg", LogoInk.Black, LogoBack.White, width, "jpg"));
        }
        return files;
    }

    /// <summary>What the person should know about the master before sending the pack, or null when it is fine.</summary>
    public static string? Warning(int masterWidth, bool vector, bool seeThrough)
    {
        var notes = new List<string>();
        if (!vector && masterWidth < Widths[0])
            notes.Add($"The logo is {masterWidth:N0} pixels wide, so nothing bigger than that was made. For print sizes, start from an SVG or a picture at least {Widths[0]:N0} wide.");
        if (!seeThrough)
            notes.Add("It has no see-through background, so the all-white and all-black versions were left out. A logo saved without a background gives the full pack.");
        return notes.Count == 0 ? null : string.Join(" ", notes);
    }

    /// <summary>The note that goes in the pack, telling the client which file to use where.</summary>
    public static string ReadMe(string? name, bool vectorIncluded, bool seeThrough)
    {
        var text = new StringBuilder();
        text.AppendLine((string.IsNullOrWhiteSpace(name) ? "Your" : name.Trim()) + " logo files");
        text.AppendLine();
        text.AppendLine("Which file to use:");
        text.AppendLine();
        if (vectorIncluded)
            text.AppendLine("Vector folder: the SVG. Give this to printers, signwriters and designers. It stays sharp at any size.");
        if (seeThrough)
            text.AppendLine("Colour folder, PNG: the logo with a see-through background, for websites, documents and presentations. WebP is the same, smaller, for websites.");
        text.AppendLine("Colour folder, JPG on white: for places that need a plain picture, like some forms and directories.");
        if (seeThrough)
        {
            text.AppendLine("White folder: the logo all in white, for dark backgrounds and photos.");
            text.AppendLine("Black folder: the logo all in black, for one-colour printing, stamps and faxes.");
        }
        text.AppendLine();
        text.AppendLine("The number in each name is the width in pixels. Use 2000 for print and large banners, 1000 or 500 for websites and documents, 250 for mail signatures and small spots.");
        text.AppendLine("Don't stretch a small file to make it bigger: pick a bigger one, or the SVG.");
        return text.ToString();
    }
}

/// <summary>A picture in a favicon set: its file name, its size in pixels, and whether it has a filled background.</summary>
public sealed record FaviconFile(string Name, int Size, bool Filled);

/// <summary>
/// The favicon set: the icons browsers, phones and search results ask for, the small manifest that goes with
/// them, and the lines to paste into the page's head.
/// </summary>
public static class Favicons
{
    /// <summary>The sizes kept inside favicon.ico, which old browsers and Windows shortcuts read.</summary>
    public static IReadOnlyList<int> IcoSizes { get; } = [16, 32, 48];

    /// <summary>
    /// The PNGs. The Apple one is filled, as iPhones put a black square behind a see-through icon; so is the
    /// "maskable" one Android crops to a circle or squircle.
    /// </summary>
    public static IReadOnlyList<FaviconFile> Files { get; } =
    [
        new("favicon-16x16.png", 16, false),
        new("favicon-32x32.png", 32, false),
        new("favicon-96x96.png", 96, false),
        new("apple-touch-icon.png", 180, true),
        new("android-chrome-192x192.png", 192, false),
        new("android-chrome-512x512.png", 512, false),
        new("maskable-512x512.png", 512, true),
    ];

    /// <summary>How much of a filled icon's square is left clear round the logo, each side. Android may crop up to a tenth.</summary>
    public const double FilledMargin = 0.14;

    /// <summary>
    /// A favicon.ico holding several PNGs, smallest first: a 6-byte header, a 16-byte entry for each picture,
    /// then the pictures themselves. Every browser that still reads .ico reads PNGs inside one.
    /// </summary>
    public static byte[] Ico(IReadOnlyList<(int Size, byte[] Png)> pictures)
    {
        var total = 6 + 16 * pictures.Count + pictures.Sum(p => p.Png.Length);
        var ico = new byte[total];
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4), (ushort)pictures.Count);
        var at = 6 + 16 * pictures.Count;
        for (var i = 0; i < pictures.Count; i++)
        {
            var (size, png) = pictures[i];
            var entry = ico.AsSpan(6 + 16 * i, 16);
            // 256 is written as 0; nothing here is that big, but the format says so.
            entry[0] = entry[1] = (byte)(size >= 256 ? 0 : size);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)png.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)at);
            png.CopyTo(ico, at);
            at += png.Length;
        }
        return ico;
    }

    /// <summary>The lines for the page's head. With an SVG, modern browsers use it and fall back to the others.</summary>
    public static string Html(bool svg, string themeColour)
    {
        var html = new StringBuilder();
        html.Append("<link rel=\"icon\" href=\"/favicon.ico\" sizes=\"48x48\">\n");
        if (svg)
            html.Append("<link rel=\"icon\" href=\"/favicon.svg\" type=\"image/svg+xml\">\n");
        html.Append("<link rel=\"icon\" type=\"image/png\" sizes=\"32x32\" href=\"/favicon-32x32.png\">\n");
        html.Append("<link rel=\"icon\" type=\"image/png\" sizes=\"16x16\" href=\"/favicon-16x16.png\">\n");
        html.Append("<link rel=\"apple-touch-icon\" href=\"/apple-touch-icon.png\">\n");
        html.Append("<link rel=\"manifest\" href=\"/site.webmanifest\">\n");
        html.Append(CultureInfo.InvariantCulture, $"<meta name=\"theme-color\" content=\"{themeColour}\">");
        return html.ToString();
    }

    private static readonly JsonSerializerOptions Laid = new() { WriteIndented = true };

    /// <summary>site.webmanifest: the site's name and its icons, for "Add to home screen" on phones.</summary>
    public static string Manifest(string? name, string themeColour, string backgroundColour)
    {
        var full = string.IsNullOrWhiteSpace(name) ? "Website" : name.Trim();
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = full,
            ["short_name"] = full.Length <= 12 ? full : full.Split(' ')[0],
            ["icons"] = new object[]
            {
                new Dictionary<string, string> { ["src"] = "/android-chrome-192x192.png", ["sizes"] = "192x192", ["type"] = "image/png" },
                new Dictionary<string, string> { ["src"] = "/android-chrome-512x512.png", ["sizes"] = "512x512", ["type"] = "image/png" },
                new Dictionary<string, string> { ["src"] = "/maskable-512x512.png", ["sizes"] = "512x512", ["type"] = "image/png", ["purpose"] = "maskable" },
            },
            ["theme_color"] = themeColour,
            ["background_color"] = backgroundColour,
            ["display"] = "standalone",
        }, Laid);
    }

    /// <summary>What to tell the person once the set is saved.</summary>
    public const string WhereItGoes = "Upload every file to the top folder of the website (beside the home page), then paste the lines into the head of each page, or the theme's header.";
}
