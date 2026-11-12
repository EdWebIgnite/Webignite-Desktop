using System.Globalization;
using System.Text.RegularExpressions;

namespace Webignite.Core.Mail;

/// <summary>
/// Makes mail as Outlook, Word and newsletter tools write it fit the app's light viewer, which knows less than a
/// browser. Only how the mail is shown here changes: the mail itself, and what a reply quotes, are left alone.
///
/// What it puts right:
/// - sizes given in inches, centimetres or points (Word sizes pictures that way), which the viewer ignores, so a
///   signature's logo was drawn at its full size;
/// - pictures and tables wider than the reading pane, which ran off the side: a wide picture now shrinks to fit,
///   keeping its shape, and a table made 600 wide for a browser takes the width there is, up to its own;
/// - Word's own tags (&lt;o:p&gt;) and its notes to Outlook (&lt;!--[if mso]&gt;), which swallowed blank lines or
///   showed as stray text;
/// - links dressed as buttons ("Pay now", "Review and approve"), which lost their padding and height, or vanished.
/// </summary>
public static partial class MailTidy
{
    /// <summary>A table or cell at least this wide was sized for a browser window, not for a reading pane.</summary>
    public const int WideFrom = 400;

    /// <summary>A picture at least this big both ways is a picture, not a spacer used to push things apart.</summary>
    public const int PictureFrom = 40;

    // A paragraph holding nothing but a space is how Word writes a blank line; the viewer gives it no height.
    [GeneratedRegex(@"(?<open><p\b[^>]*>(?:\s*<(?:span|b|i|font)\b[^>]*>)*)\s*(?:&nbsp;|\u00A0)\s*(?<close>(?:</(?:span|b|i|font)>\s*)*</p>)", RegexOptions.IgnoreCase)]
    private static partial Regex BlankParagraphs();

    [GeneratedRegex(@"</?o:p\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex WordParagraphMarks();

    // Notes meant for Outlook alone. The "if !mso" kind holds what everyone else should see, so its content stays.
    [GeneratedRegex(@"<!--\[if\s+(?!\s*!mso)[^\]]*\]>(?!\s*<!-->).*?<!\[endif\]-->", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OutlookOnly();

    [GeneratedRegex(@"<!--\[if[^\]]*\]>\s*<!-->|<!--\[if[^\]]*\]>|<!--\s*<!\[endif\]-->|<!\[endif\]-->", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionMarks();

    [GeneratedRegex(@"<(?<tag>img|table|td|th)\b(?<rest>[^>]*)>|(?<end></table\s*>)", RegexOptions.IgnoreCase)]
    private static partial Regex SizedTags();

    [GeneratedRegex(@"\sstyle\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex(@"\s(?<name>width|height)\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex SizeAttribute();

    [GeneratedRegex(@"(?<![\w-])(?<name>(?:max-|min-)?(?:width|height))\s*:\s*(?<n>\d*\.?\d+)\s*(?<unit>in|cm|mm|pt|pc)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherUnits();

    [GeneratedRegex(@"(?<![\w-])(?<name>width|height|max-width)\s*:\s*(?<v>[^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Declarations();

    public static string Tidy(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;
        var tidy = OutlookOnly().Replace(html, "");
        tidy = ConditionMarks().Replace(tidy, "");
        tidy = WordParagraphMarks().Replace(tidy, "");
        tidy = BlankParagraphs().Replace(tidy, "${open}<br>${close}");
        tidy = ButtonLinks().Replace(tidy, AsButton);
        // Which of the tables we are inside were made to give way, innermost last: their cells give way with them.
        var tables = new Stack<bool>();
        return SizedTags().Replace(tidy, m =>
        {
            if (m.Groups["end"].Success)
            {
                if (tables.Count > 0)
                    tables.Pop();
                return m.Value;
            }
            var tag = m.Groups["tag"].Value.ToLowerInvariant();
            // A tag closed in itself (<img ... />) is written without the slash; the viewer reads both the same.
            var (attributes, before, after, gaveWay) = Sized(tag, m.Groups["rest"].Value.TrimEnd().TrimEnd('/'), tables.Count > 0 && tables.Peek());
            if (tag == "table")
                tables.Push(gaveWay);
            return before + "<" + m.Groups["tag"].Value + attributes + ">" + after;
        });
    }

    [GeneratedRegex(@"<a\b(?<attrs>[^>]*)>(?<inner>(?:(?!</?a\b|<table\b|<div\b|<p\b).)*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ButtonLinks();

    [GeneratedRegex(@"^(#[0-9a-f]{3,8}|rgba?\([^)]*\)|[a-z]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PlainColour();

    // A link made to look like a button (a background colour, with padding or a height) is how most senders write
    // "Pay now" and "Review and approve". The viewer gives a link no padding or height, so the words sat jammed in
    // a strip, or the button vanished. A one-cell table of the same colour and size is something it draws properly.
    private static string AsButton(Match link)
    {
        var attributes = link.Groups["attrs"].Value;
        if (StyleAttribute().Match(attributes) is not { Success: true } styled)
            return link.Value;
        var parts = styled.Groups["v"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Split(':', 2)).Where(d => d.Length == 2).Select(d => (Name: d[0].Trim().ToLowerInvariant(), Value: d[1].Trim())).ToList();
        string? Value(params string[] names) => parts.LastOrDefault(d => names.Contains(d.Name)).Value;
        var colour = Value("background-color", "background")?.Replace("!important", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (colour is null || !PlainColour().IsMatch(colour) || colour.ToLowerInvariant() is "transparent" or "none" or "inherit" or "initial")
            return link.Value;
        var padding = Value("padding");
        var height = PixelsOf(Value("height")) ?? PixelsOf(Value("line-height"));
        if (padding is null && height is not >= 24 && !parts.Any(d => d.Name.StartsWith("padding-", StringComparison.Ordinal)))
            return link.Value; // a highlighted word, not a button

        string[] boxOnly = ["background", "background-color", "padding", "display", "height", "line-height", "width", "min-width", "max-width", "border", "border-radius", "box-sizing", "margin"];
        var cell = new List<string> { "background-color:" + colour };
        cell.Add("padding:" + (padding ?? (height >= 24 ? "0 20px" : "10px 20px")));
        cell.AddRange(parts.Where(d => d.Name.StartsWith("padding-", StringComparison.Ordinal) || d.Name is "border" or "border-radius").Select(d => d.Name + ":" + d.Value));
        var text = string.Join(";", parts.Where(d => !boxOnly.Contains(d.Name) && !d.Name.StartsWith("padding-", StringComparison.Ordinal) && !d.Name.StartsWith("margin-", StringComparison.Ordinal))
            .Select(d => d.Name + ":" + d.Value));
        var size = (height >= 24 ? $" height=\"{Math.Round(height.Value).ToString(CultureInfo.InvariantCulture)}\"" : "")
            + (PixelsOf(Value("width")) is >= 24 and var width ? $" width=\"{Math.Round(width).ToString(CultureInfo.InvariantCulture)}\"" : "");
        var rest = StyleAttribute().Replace(attributes, "").TrimEnd();
        return $"<table cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td align=\"center\" valign=\"middle\"{size} style=\"{string.Join(";", cell).Replace("\"", "'", StringComparison.Ordinal)}\">"
            + $"<a{rest}{(text.Length > 0 ? " style=\"" + text.Replace("\"", "'", StringComparison.Ordinal) + "\"" : "")}>{link.Groups["inner"].Value}</a></td></tr></table>";
    }

    /// <summary>A length in the units Word uses, as pixels: 2.5in is 240.</summary>
    public static double Pixels(double number, string unit) => unit.ToLowerInvariant() switch
    {
        "in" => number * 96,
        "cm" => number * 96 / 2.54,
        "mm" => number * 96 / 25.4,
        "pt" => number * 96 / 72,
        "pc" => number * 16,
        _ => number,
    };

    private static string Px(double pixels) => Math.Round(pixels).ToString(CultureInfo.InvariantCulture) + "px";

    // One tag's attributes, with its sizes made ones the viewer follows and that fit the pane.
    private static (string Attributes, string Before, string After, bool GaveWay) Sized(string tag, string attributes, bool inTableThatGaveWay)
    {
        var style = StyleAttribute().Match(attributes) is { Success: true } s ? s.Groups["v"].Value : "";
        style = OtherUnits().Replace(style, m => m.Groups["name"].Value + ":" +
            Px(Pixels(double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture), m.Groups["unit"].Value)));
        var styled = Declarations().Matches(style).ToDictionary(m => m.Groups["name"].Value.ToLowerInvariant(), m => m.Groups["v"].Value.Trim(), StringComparer.Ordinal);
        var typed = SizeAttribute().Matches(attributes).ToDictionary(m => m.Groups["name"].Value.ToLowerInvariant(), m => m.Groups["v"].Value.Trim(), StringComparer.Ordinal);
        // How wide it asks to be, in pixels; null when it doesn't say, or says so as a share of the room.
        double? width = PixelsOf(styled.GetValueOrDefault("width")) ?? PixelsOf(typed.GetValueOrDefault("width"));

        if (tag == "img")
        {
            double? height = PixelsOf(styled.GetValueOrDefault("height")) ?? PixelsOf(typed.GetValueOrDefault("height"));
            var stated = width ?? (styled.ContainsKey("width") || typed.ContainsKey("width") ? null : NaturalWidth(attributes));
            // A wide, thin spacer (there to push things apart) is as wide as its room and keeps its height.
            if (stated >= WideFrom && height < PictureFrom)
            {
                attributes = Without(StyleAttribute().Replace(attributes, ""), "width");
                style = Join(WithoutDeclaration(WithoutDeclaration(style, "width"), "max-width"), "width:100%");
                return (attributes.TrimEnd() + " style=\"" + style.Replace("\"", "'", StringComparison.Ordinal) + "\"", "", "", false);
            }
            // A small picture is drawn at the size it states.
            if (stated is not >= WideFrom)
            {
                attributes = StyleAttribute().Replace(attributes, "");
                return (style.Trim().Length == 0 ? attributes : attributes.TrimEnd() + " style=\"" + style.Replace("\"", "'", StringComparison.Ordinal).Trim() + "\"", "", "", false);
            }
            // A wide picture fills a wrapper that takes the room there is, up to the picture's own width. The viewer
            // keeps a picture's shape when its width is a share of its room and its height is left to follow, which
            // it doesn't do for a plain "no wider than"; and the wrapper stops a small pane's picture being blown up
            // in a big one.
            attributes = Without(Without(StyleAttribute().Replace(attributes, ""), "height"), "width");
            style = Join(WithoutDeclaration(WithoutDeclaration(WithoutDeclaration(style, "height"), "width"), "max-width"), "width:100%;height:auto");
            return (attributes.TrimEnd() + " style=\"" + style.Replace("\"", "'", StringComparison.Ordinal) + "\"",
                $"<table cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:{Px(stated.Value)};border-collapse:collapse\"><tr><td style=\"padding:0\">",
                "</td></tr></table>", false);
        }
        var gaveWay = false;
        if (width >= WideFrom)
        {
            attributes = Without(attributes, "width");
            style = WithoutDeclaration(WithoutDeclaration(style, "width"), "max-width");
            // A wide table takes the room there is, up to the width it was made for; a wide cell follows its table.
            if (tag == "table")
            {
                style = Join(style, $"width:100%;max-width:{Px(width.Value)}");
                gaveWay = true;
            }
        }
        else if (tag != "table" && inTableThatGaveWay && width >= PictureFrom)
        {
            // In a table that had to give way, a column's fixed width would be kept at its neighbours' cost (a
            // signature's logo squeezed thin beside its text), so columns share the room by what is in them.
            // Narrow ones, there as gaps, keep theirs.
            attributes = Without(attributes, "width");
            style = WithoutDeclaration(style, "width");
        }

        attributes = StyleAttribute().Replace(attributes, "");
        return (style.Trim().Length == 0 ? attributes : attributes.TrimEnd() + " style=\"" + style.Replace("\"", "'", StringComparison.Ordinal).Trim() + "\"", "", "", gaveWay);
    }

    [GeneratedRegex(@"\ssrc\s*=\s*[""']?data:image/(?<kind>png|gif|jpeg|jpg);base64,(?<data>[A-Za-z0-9+/=]{24,})", RegexOptions.IgnoreCase)]
    private static partial Regex EmbeddedPicture();

    // How wide a picture carried inside the mail really is, read from its first bytes; null when it can't be told.
    private static double? NaturalWidth(string attributes)
    {
        if (EmbeddedPicture().Match(attributes) is not { Success: true } m)
            return null;
        var data = m.Groups["data"].Value;
        var kind = m.Groups["kind"].Value.ToLowerInvariant();
        // Only as much as holds the size: the start for PNG and GIF, further in for JPEG.
        var take = Math.Min(data.Length, kind is "png" or "gif" ? 44 : 87_380) / 4 * 4;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(data[..take]);
        }
        catch (FormatException)
        {
            return null;
        }
        return PictureWidth(bytes);
    }

    /// <summary>The width of a PNG, GIF or JPEG from the bytes at its start; null for anything else or too few bytes.</summary>
    public static double? PictureWidth(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G')
            return (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        if (bytes.Length >= 10 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F')
            return bytes[6] | (bytes[7] << 8);
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            var at = 2;
            while (at + 9 < bytes.Length && bytes[at] == 0xFF)
            {
                var marker = bytes[at + 1];
                // The frame headers hold the size; the ones skipped here are tables, not frames.
                if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                    return (bytes[at + 7] << 8) | bytes[at + 8];
                at += 2 + ((bytes[at + 2] << 8) | bytes[at + 3]);
            }
        }
        return null;
    }

    private static double? PixelsOf(string? value)
    {
        if (value is null)
            return null;
        var v = value.Replace("!important", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            v = v[..^2];
        return double.TryParse(v, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static string Without(string attributes, string name) =>
        SizeAttribute().Replace(attributes, m => m.Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase) ? "" : m.Value);

    private static string WithoutDeclaration(string style, string name) =>
        string.Join(";", style.Split(';').Where(d => d.Trim().Length > 0 && !d.TrimStart().StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)
            && !(d.TrimStart().StartsWith(name, StringComparison.OrdinalIgnoreCase) && d.TrimStart()[name.Length..].TrimStart().StartsWith(':'))));

    private static string Join(string style, string more) => style.Trim().Length == 0 ? more : style.Trim().TrimEnd(';') + ";" + more;
}
