using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Webignite.Core.Mail;

/// <summary>Text with the selection after an edit, for the mail editor's format buttons.</summary>
public sealed record FormatEdit(string Text, int SelectionStart, int SelectionEnd);

/// <summary>
/// The light formatting the mail editor understands: **bold**, *italic*, [words](link), lines starting with
/// "- " for a bulleted list and "1. " for a numbered one. Typed as plain text, sent as HTML, so it works in
/// every mail program. Bare web addresses become links too.
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

    /// <summary>Puts markers round the selection, or takes them off if they're already there. With nothing selected the caret goes between them.</summary>
    public static FormatEdit Wrap(string text, int start, int end, string marker)
    {
        (start, end) = Order(text, start, end);
        // Leave spaces outside the markers: "**word **" isn't bold anywhere.
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;
        while (start < end && char.IsWhiteSpace(text[start]))
            start++;
        var m = marker.Length;
        if (start >= m && end + m <= text.Length && text.AsSpan(start - m, m).SequenceEqual(marker) && text.AsSpan(end, m).SequenceEqual(marker)
            && !(marker == "*" && IsBold(text, start, end)))
            return new(text.Remove(end, m).Remove(start - m, m), start - m, end - m);
        var inner = text[start..end];
        if (inner.Length >= 2 * m && inner.StartsWith(marker, StringComparison.Ordinal) && inner.EndsWith(marker, StringComparison.Ordinal))
            return new(text[..start] + inner[m..^m] + text[end..], start, end - 2 * m);
        return new(text[..start] + marker + inner + marker + text[end..], start + m, end + m);
    }

    // "**word**" with the caret inside the inner stars: italic shouldn't eat half the bold.
    private static bool IsBold(string text, int start, int end) =>
        start >= 2 && end + 2 <= text.Length && text[start - 2] == '*' && text[end + 1] == '*';

    /// <summary>Turns the lines under the selection into a list, numbering them if asked, or back into plain lines.</summary>
    public static FormatEdit List(string text, int start, int end, bool numbered)
    {
        (start, end) = Order(text, start, end);
        var first = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var stop = end > first && end > 0 && text[end - 1] == '\n' ? end - 1 : end;
        var last = text.IndexOf('\n', stop);
        if (last < 0)
            last = text.Length;
        var lines = text[first..last].Split('\n');
        var kind = numbered ? "ol" : "ul";
        var already = lines.Where(l => l.Trim().Length > 0).All(l => ListItem(l).Kind == kind) && lines.Any(l => l.Trim().Length > 0);
        var number = 0;
        var changed = lines.Select(line =>
        {
            var item = ListItem(line).Item;
            if (already)
                return item;
            if (line.Trim().Length == 0 && lines.Length > 1)
                return line;
            return numbered ? $"{++number}. {item}" : $"- {item}";
        });
        var block = string.Join('\n', changed);
        var result = text[..first] + block + text[last..];
        return lines.Length == 1 && lines[0].Trim().Length == 0 && !already
            ? new(result, first + block.Length, first + block.Length)
            : new(result, first, first + block.Length);
    }

    /// <summary>Makes the selection a link. With nothing selected the address itself is the link text.</summary>
    public static FormatEdit Link(string text, int start, int end, string url)
    {
        (start, end) = Order(text, start, end);
        url = url.Trim();
        if (!url.Contains("://", StringComparison.Ordinal) && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            url = url.Contains('@') && !url.Contains('/') ? "mailto:" + url : "https://" + url;
        var words = text[start..end].Trim();
        if (words.Length == 0)
            words = url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? url[7..] : url.Replace("https://", "", StringComparison.OrdinalIgnoreCase);
        var link = $"[{words}]({url})";
        return new(text[..start] + link + text[end..], start + link.Length, start + link.Length);
    }

    private static (int, int) Order(string text, int start, int end)
    {
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        return start <= end ? (start, end) : (end, start);
    }

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
