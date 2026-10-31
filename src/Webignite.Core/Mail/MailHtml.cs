using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Webignite.Core.Mail;

/// <summary>
/// Makes mail HTML safe to show. The viewer never runs scripts, but mail can still try to track you
/// with remote pictures or trick you with forms, so those are taken out before anything is drawn.
/// </summary>
public static partial class MailHtml
{
    [GeneratedRegex(@"<(script|iframe|object|embed|form|frame|frameset|applet|meta|link|base)\b[^>]*>(.*?</\1\s*>)?", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Dangerous();

    [GeneratedRegex(@"\s+on[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex EventAttributes();

    [GeneratedRegex(@"(href|src)\s*=\s*([""']?)\s*(javascript|vbscript):[^""'\s>]*\2", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptLinks();

    [GeneratedRegex(@"<img\b[^>]*?\bsrc\s*=\s*([""']?)(https?:)?//[^""'\s>]+\1[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteImages();

    [GeneratedRegex(@"url\(\s*([""']?)(https?:)?//[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteCssUrls();

    [GeneratedRegex(@"<(br|/p|/div|/tr|/h[1-6]|/li)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"<(style|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleAndHead();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n\s*\n\s*\n+")]
    private static partial Regex BlankLines();

    public static bool HasRemoteImages(string html) => RemoteImages().IsMatch(html) || RemoteCssUrls().IsMatch(html);

    public static string Sanitise(string html, bool allowRemoteImages)
    {
        var safe = Dangerous().Replace(html, "");
        safe = EventAttributes().Replace(safe, "");
        safe = ScriptLinks().Replace(safe, "$1=\"#\"");
        if (!allowRemoteImages)
        {
            safe = RemoteImages().Replace(safe, "");
            safe = RemoteCssUrls().Replace(safe, "none");
        }
        return safe;
    }

    /// <summary>Plain text from HTML, for previews, search and what Claude and ChatGPT read.</summary>
    public static string ToText(string html)
    {
        var text = StyleAndHead().Replace(html, "");
        text = LineBreaks().Replace(text, "\n");
        text = Tags().Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ').Replace("\r", "");
        text = Spaces().Replace(text, " ");
        text = string.Join('\n', text.Split('\n').Select(l => l.Trim()));
        return BlankLines().Replace(text, "\n\n").Trim();
    }

    /// <summary>Shows plain-text mail with the same look as HTML mail, keeping line breaks.</summary>
    public static string FromText(string text)
    {
        var sb = new StringBuilder("<div style=\"white-space: pre-wrap\">");
        sb.Append(WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Replace("\n", "<br>"));
        sb.Append("</div>");
        return sb.ToString();
    }
}

public static class MailText
{
    public static string Preview(string text, int length = 140)
    {
        var flat = string.Join(' ', text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= length ? flat : flat[..length].TrimEnd() + "…";
    }

    /// <summary>"Re: Invoice" stays "Re: Invoice", and "RE: Fwd: x" isn't stacked again.</summary>
    public static string ReplySubject(string subject) => Prefixed("Re: ", subject, "re:");

    public static string ForwardSubject(string subject) => Prefixed("Fwd: ", subject, "fwd:", "fw:");

    private static string Prefixed(string prefix, string subject, params string[] existing)
    {
        var trimmed = subject.Trim();
        return existing.Any(e => trimmed.StartsWith(e, StringComparison.OrdinalIgnoreCase)) ? trimmed : prefix + trimmed;
    }
}
