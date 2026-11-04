using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Webignite.Core.Mail;

/// <summary>
/// The light marks plain-text mail can carry: **bold**, *italic*, [words](link), lines starting with "- " for a
/// bulleted list and "1. " for a numbered one. Templates, assistants' drafts and older drafts use them; mail
/// written in the editor is formatted there instead (see Webignite.Core.Writing).
/// </summary>
public static partial class MailFormat
{
    private const string LinkStyle = "color:#1a5fb4";

    /// <summary>The HTML part of the mail: lists become lists, the rest keeps its line breaks.</summary>
    public static string ToHtml(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var html = new StringBuilder("<div style=\"white-space: pre-wrap\">");
        string? open = null;
        var afterList = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var (kind, item) = ListItem(line);
            if (kind != open)
            {
                if (open is not null)
                    html.Append("</").Append(open).Append('>');
                if (kind is not null)
                    html.Append('<').Append(kind).Append(" style=\"margin:4px 0 4px 0; padding-left:24px\">");
                afterList = open is not null && kind is null;
                open = kind;
            }
            if (kind is not null)
            {
                html.Append("<li>").Append(Inline(item)).Append("</li>");
                continue;
            }
            // A list already ends the line, so the line break after it would leave a gap.
            if (i > 0 && !afterList)
                html.Append("<br>");
            afterList = false;
            html.Append(Inline(line));
        }
        if (open is not null)
            html.Append("</").Append(open).Append('>');
        return html.Append("</div>").ToString();
    }

    /// <summary>The plain-text part: stars stay (people read them as emphasis), links show their address.</summary>
    public static string ToText(string text) =>
        MarkdownLink().Replace(text, m => m.Groups[1].Value == m.Groups[2].Value ? m.Groups[2].Value : $"{m.Groups[1].Value} ({m.Groups[2].Value})");

    /// <summary>Whether a line is a list item, and which kind: "ul", "ol" or null.</summary>
    public static (string? Kind, string Item) ListItem(string line)
    {
        if (BulletLine().Match(line) is { Success: true } bullet)
            return ("ul", bullet.Groups[1].Value);
        if (NumberLine().Match(line) is { Success: true } number)
            return ("ol", number.Groups[1].Value);
        return (null, line);
    }

    private static string Inline(string text)
    {
        // Pull links out first so their addresses don't pick up bold or italic.
        var links = new List<string>();
        string Hold(string html)
        {
            links.Add(html);
            return $"\u0001{links.Count - 1}\u0002";
        }

        var held = MarkdownLink().Replace(text, m => Hold(Anchor(m.Groups[2].Value, m.Groups[1].Value)));
        held = BareLink().Replace(held, m =>
        {
            var url = m.Value.TrimEnd('.', ',', ')', ';', ':', '!', '?');
            var tail = m.Value[url.Length..];
            return Hold(Anchor(url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + url : url, url)) + tail;
        });

        var html = WebUtility.HtmlEncode(held);
        html = Bold().Replace(html, "<b>$1</b>");
        html = Italic().Replace(html, "<i>$1</i>");
        return Held().Replace(html, m => links[int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]);
    }

    private static string Anchor(string url, string words) =>
        $"<a href=\"{WebUtility.HtmlEncode(url)}\" style=\"{LinkStyle}\">{WebUtility.HtmlEncode(words)}</a>";

    [GeneratedRegex(@"^\s*[-*•]\s+(.*)$")]
    private static partial Regex BulletLine();

    [GeneratedRegex(@"^\s*\d{1,3}[.)]\s+(.*)$")]
    private static partial Regex NumberLine();

    [GeneratedRegex(@"\[([^\]\n]+)\]\(((?:https?://|mailto:)[^\s)]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"\b(?:https?://|www\.)[^\s<>""\u0001\u0002]+", RegexOptions.IgnoreCase)]
    private static partial Regex BareLink();

    [GeneratedRegex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![*\w])\*(?=[^\s*])(.+?)(?<=[^\s*])\*(?![*\w])")]
    private static partial Regex Italic();

    [GeneratedRegex("\u0001(\\d+)\u0002")]
    private static partial Regex Held();
}
