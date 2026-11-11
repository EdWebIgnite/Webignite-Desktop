using System.Net;
using System.Text;

namespace Webignite.Core.Web;

/// <summary>A link on a page: where it goes (as written, not yet made whole) and the words on it.</summary>
public sealed record PageLink(string Href, string Text, bool NoFollow);

/// <summary>What a search engine and a visitor's browser read from a page's HTML.</summary>
public sealed record PageFacts
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<string> Headings { get; init; } = [];
    public string Canonical { get; init; } = "";

    /// <summary>The page asks search engines not to list it.</summary>
    public bool NoIndex { get; init; }

    /// <summary>A &lt;base href&gt;, which the page's links are worked out from when it has one.</summary>
    public string Base { get; init; } = "";

    public IReadOnlyList<PageLink> Links { get; init; } = [];

    /// <summary>The pictures on the page (their src), and those with no alt text at all.</summary>
    public IReadOnlyList<string> Images { get; init; } = [];
    public IReadOnlyList<string> ImagesWithoutAlt { get; init; } = [];

    /// <summary>Stylesheets and scripts the page loads, checked like links.</summary>
    public IReadOnlyList<string> Assets { get; init; } = [];
}

/// <summary>
/// Reads the facts the site crawler reports from a page's HTML. Not a browser: it reads the HTML as sent, so a
/// page that fills itself in with JavaScript shows only what the server sent. Forgiving of sloppy HTML, as real
/// pages are; it never throws on a page, however broken.
/// </summary>
public static class HtmlFacts
{
    public static PageFacts Read(string? html)
    {
        var text = html ?? "";
        string title = "", description = "", canonical = "", @base = "";
        var noIndex = false;
        var headings = new List<string>();
        var links = new List<PageLink>();
        var images = new List<string>();
        var noAlt = new List<string>();
        var assets = new List<string>();

        // Words are gathered while inside a <title>, an <h1> or an <a>.
        StringBuilder? titleWords = null, headingWords = null, linkWords = null;
        (string Href, bool NoFollow) link = ("", false);

        var at = 0;
        while (at < text.Length)
        {
            var open = text.IndexOf('<', at);
            if (open < 0)
            {
                Words(text.AsSpan(at));
                break;
            }
            Words(text.AsSpan(at, open - at));
            if (text.AsSpan(open).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = text.IndexOf("-->", open + 4, StringComparison.Ordinal);
                at = end < 0 ? text.Length : end + 3;
                continue;
            }
            var close = TagEnd(text, open);
            if (close < 0)
                break;
            var tag = text.AsSpan(open + 1, close - open - 1);
            at = close + 1;
            var closing = tag.StartsWith("/");
            if (closing)
                tag = tag[1..];
            var nameEnd = 0;
            while (nameEnd < tag.Length && (char.IsAsciiLetterOrDigit(tag[nameEnd]) || tag[nameEnd] is '-' or ':'))
                nameEnd++;
            if (nameEnd == 0)
                continue; // <!doctype>, <?xml?>, a stray <
            var name = tag[..nameEnd].ToString().ToLowerInvariant();

            if (closing)
            {
                switch (name)
                {
                    case "title" when titleWords is not null:
                        if (title.Length == 0)
                            title = Tidy(titleWords);
                        titleWords = null;
                        break;
                    case "h1" when headingWords is not null:
                        headings.Add(Tidy(headingWords));
                        headingWords = null;
                        break;
                    case "a" when linkWords is not null:
                        links.Add(new PageLink(link.Href, Tidy(linkWords), link.NoFollow));
                        linkWords = null;
                        break;
                }
                continue;
            }

            var attributes = Attributes(tag[nameEnd..]);
            string Get(string key) => attributes.GetValueOrDefault(key, "").Trim();
            switch (name)
            {
                case "title":
                    titleWords = new StringBuilder();
                    break;
                case "h1":
                    headingWords = new StringBuilder();
                    break;
                case "a" or "area" when Get("href").Length > 0:
                    // A link left open when the next one starts still counts.
                    if (linkWords is not null)
                        links.Add(new PageLink(link.Href, Tidy(linkWords), link.NoFollow));
                    link = (Get("href"), Has(Get("rel"), "nofollow"));
                    linkWords = name == "a" ? new StringBuilder() : null;
                    if (name == "area")
                        links.Add(new PageLink(link.Href, Get("alt"), link.NoFollow));
                    break;
                case "meta":
                    var metaName = Get("name").ToLowerInvariant();
                    if (metaName == "description" && description.Length == 0)
                        description = Tidy(Get("content"));
                    else if (metaName is "robots" or "googlebot" && Has(Get("content").Replace(',', ' '), "noindex"))
                        noIndex = true;
                    break;
                case "link":
                    var rel = Get("rel");
                    if (Has(rel, "canonical") && canonical.Length == 0)
                        canonical = Get("href");
                    else if (Has(rel, "stylesheet") && Get("href").Length > 0)
                        assets.Add(Get("href"));
                    break;
                case "base" when @base.Length == 0:
                    @base = Get("href");
                    break;
                case "img":
                    var source = Get("src");
                    if (source.Length == 0)
                        source = Get("data-src");
                    if (source.Length == 0 || source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        break;
                    images.Add(source);
                    // alt="" is right for a picture that is only decoration; no alt at all is the mistake.
                    if (!attributes.ContainsKey("alt") && !Has(Get("role"), "presentation") && Get("aria-hidden") != "true")
                        noAlt.Add(source);
                    linkWords?.Append(' ').Append(Get("alt")).Append(' ');
                    break;
                case "script" or "style":
                    if (name == "script" && Get("src").Length > 0)
                        assets.Add(Get("src"));
                    // What is inside isn't HTML: skip to where it closes.
                    var ends = text.IndexOf("</" + name, at, StringComparison.OrdinalIgnoreCase);
                    at = ends < 0 ? text.Length : ends;
                    break;
            }
        }
        // What was still open when the page ran out counts as far as it got.
        if (titleWords is not null && title.Length == 0)
            title = Tidy(titleWords);
        if (linkWords is not null)
            links.Add(new PageLink(link.Href, Tidy(linkWords), link.NoFollow));

        return new PageFacts
        {
            Title = title, Description = description, Headings = headings, Canonical = canonical, NoIndex = noIndex, Base = @base,
            Links = links, Images = images, ImagesWithoutAlt = noAlt, Assets = assets,
        };

        void Words(ReadOnlySpan<char> words)
        {
            titleWords?.Append(words);
            headingWords?.Append(words);
            linkWords?.Append(words);
        }
    }

    // Where a tag ends: the first > that isn't inside a quoted value.
    private static int TagEnd(string text, int open)
    {
        var quote = '\0';
        for (var i = open + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
            }
            else if (c is '"' or '\'')
                quote = c;
            else if (c == '>')
                return i;
        }
        return -1;
    }

    private static Dictionary<string, string> Attributes(ReadOnlySpan<char> text)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == '/'))
                i++;
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('=' or '/' or '>'))
                i++;
            if (i == start)
            {
                i++;
                continue;
            }
            var key = text[start..i].ToString();
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            var value = "";
            if (i < text.Length && text[i] == '=')
            {
                i++;
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;
                if (i < text.Length && text[i] is '"' or '\'')
                {
                    var quote = text[i];
                    var end = text[(i + 1)..].IndexOf(quote);
                    end = end < 0 ? text.Length : i + 1 + end;
                    value = text[(i + 1)..end].ToString();
                    i = Math.Min(text.Length, end + 1);
                }
                else
                {
                    start = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]))
                        i++;
                    value = text[start..i].ToString();
                }
            }
            found.TryAdd(key, WebUtility.HtmlDecode(value));
        }
        return found;
    }

    private static bool Has(string list, string word) =>
        list.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(word, StringComparer.OrdinalIgnoreCase);

    private static string Tidy(StringBuilder words) => Tidy(words.ToString());

    // Entities turned into their characters, and any run of space or line breaks into one space.
    private static string Tidy(string words) =>
        string.Join(' ', WebUtility.HtmlDecode(words).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
