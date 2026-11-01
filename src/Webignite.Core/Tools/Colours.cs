using System.Globalization;

namespace Webignite.Core.Tools;

/// <summary>A colour as web work needs it: hex, RGB and HSL, and how readable text is on it.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public string Css => $"rgb({R}, {G}, {B})";

    public string Hsl
    {
        get
        {
            var (h, s, l) = Colours.ToHsl(this);
            return string.Create(CultureInfo.InvariantCulture, $"hsl({Math.Round(h)}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)");
        }
    }
}

public static class Colours
{
    /// <summary>Reads #1c1d21, 1C1D21, #abc, or rgb(28, 29, 33).</summary>
    public static Rgb? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var t = text.Trim();
        if (t.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var open = t.IndexOf('(');
            var close = t.IndexOf(')');
            if (open < 0 || close < open)
                return null;
            var parts = t[(open + 1)..close].Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !parts.Take(3).All(p => byte.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                return null;
            return new Rgb(byte.Parse(parts[0], CultureInfo.InvariantCulture), byte.Parse(parts[1], CultureInfo.InvariantCulture), byte.Parse(parts[2], CultureInfo.InvariantCulture));
        }
        t = t.TrimStart('#');
        if (t.Length == 3)
            t = string.Concat(t.Select(c => $"{c}{c}"));
        if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return null;
        return new Rgb((byte)(value >> 16), (byte)(value >> 8 & 0xFF), (byte)(value & 0xFF));
    }

    public static (double H, double S, double L) ToHsl(Rgb c)
    {
        double r = c.R / 255d, g = c.G / 255d, b = c.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min)
            return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Rgb FromHsl(double h, double s, double l)
    {
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            return t < 1d / 6 ? p + (q - p) * 6 * t : t < 1d / 2 ? q : t < 2d / 3 ? p + (q - p) * (2d / 3 - t) * 6 : p;
        }
        if (s == 0)
            return new Rgb(Byte(l), Byte(l), Byte(l));
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        var hk = h / 360;
        return new Rgb(Byte(Hue(p, q, hk + 1d / 3)), Byte(Hue(p, q, hk)), Byte(Hue(p, q, hk - 1d / 3)));
    }

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);

    /// <summary>Lighter and darker versions of the colour, for hover states and backgrounds, darkest first.</summary>
    public static IReadOnlyList<Rgb> Shades(Rgb colour)
    {
        var (h, s, _) = ToHsl(colour);
        return new[] { 0.15, 0.3, 0.45, 0.6, 0.75, 0.9 }.Select(l => FromHsl(h, s, l)).ToList();
    }

    /// <summary>The WCAG contrast ratio between two colours, from 1 to 21.</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>What the WCAG rules say about text in this pairing: 4.5 for normal text, 3 for large.</summary>
    public static string Readability(double ratio) => ratio switch
    {
        >= 7 => "Very easy to read",
        >= 4.5 => "Fine for normal text",
        >= 3 => "Only for large headings",
        _ => "Too faint for text",
    };

    private static double Luminance(Rgb c)
    {
        static double Channel(byte v)
        {
            var s = v / 255d;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
