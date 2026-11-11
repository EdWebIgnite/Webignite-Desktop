using System.Globalization;
using System.Text;

namespace Webignite.Core.Web;

/// <summary>One address the crawler fetched, and what came back.</summary>
public sealed record CrawledPage
{
    public required Uri Url { get; init; }

    /// <summary>The answer's number (200, 404...), or 0 when there was no answer at all.</summary>
    public int Status { get; init; }

    /// <summary>Where it ended up, when the address sent the crawler somewhere else.</summary>
    public Uri? RedirectedTo { get; init; }

    /// <summary>Why there was no answer (timed out, no such server), in plain words.</summary>
    public string? Problem { get; init; }

    /// <summary>What the HTML said. Null for anything that isn't a page, and for a page that couldn't be read.</summary>
    public PageFacts? Facts { get; init; }

    /// <summary>The page that first linked here, to find it when it is broken.</summary>
    public Uri? FoundOn { get; init; }

    public bool Ok => Status is >= 200 and < 400;
}

/// <summary>A link or file that was only checked, not read: does it answer?</summary>
public sealed record CheckedLink(Uri Url, int Status, string? Problem)
{
    public bool Broken => Status is 0 or >= 400;
}

/// <summary>A row of the crawler's table: a page, and what is wrong with it.</summary>
public sealed record CrawlRow(CrawledPage Page, IReadOnlyList<string> Problems, IReadOnlyList<CheckedLink> BrokenLinks);

/// <summary>What robots.txt asks every crawler to stay out of.</summary>
public sealed record RobotsRules(IReadOnlyList<string> Disallowed, IReadOnlyList<string> Allowed)
{
    public static RobotsRules None { get; } = new([], []);

    /// <summary>The longest rule that fits the path decides, and Allow wins a tie, as Google reads it.</summary>
    public bool Allows(string path)
    {
        var no = Disallowed.Where(rule => Fits(rule, path)).Select(r => r.Length).DefaultIfEmpty(-1).Max();
        var yes = Allowed.Where(rule => Fits(rule, path)).Select(r => r.Length).DefaultIfEmpty(-1).Max();
        return no < 0 || yes >= no;
    }

    // A rule is the start of a path; * stands for anything and a $ at the end means "ends here".
    private static bool Fits(string rule, string path)
    {
        var exact = rule.EndsWith('$');
        var parts = (exact ? rule[..^1] : rule).Split('*');
        var at = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var found = i == 0 ? (path.StartsWith(parts[0], StringComparison.Ordinal) ? 0 : -1) : path.IndexOf(parts[i], at, StringComparison.Ordinal);
            if (found < 0)
                return false;
            at = found + parts[i].Length;
        }
        return !exact || at == path.Length || rule.EndsWith("*$", StringComparison.Ordinal);
    }
}

/// <summary>
/// The site crawler's decisions: which addresses belong to the site, which are pages worth reading, what counts
/// as a problem, and the spreadsheet. Pure; the fetching is in Services (SiteCrawler).
/// </summary>
public static class SiteCrawl
{
    public const int UsualPages = 200;
    public const int MostPages = 2000;

    /// <summary>Google shows about this much of a title and of a description before cutting them off.</summary>
    public const int TitleLength = 60;
    public const int DescriptionLength = 160;

    private static readonly HashSet<string> NotPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".svg", ".ico", ".bmp", ".pdf", ".zip", ".rar", ".7z", ".gz", ".doc", ".docx", ".xls", ".xlsx",
        ".ppt", ".pptx", ".mp3", ".mp4", ".mov", ".avi", ".webm", ".wav", ".css", ".js", ".json", ".xml", ".txt", ".csv", ".woff", ".woff2", ".ttf", ".otf", ".eot", ".exe", ".dmg", ".apk",
    };

    /// <summary>Where to start from what was typed: "karoocoffee.co.za/menu" becomes https://karoocoffee.co.za/menu.</summary>
    public static Uri? Start(string? typed)
    {
        var text = (typed ?? "").Trim();
        if (text.Length == 0 || text.Contains(' '))
            return null;
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && Domains.Of(uri.Host) is not null ? Clean(uri) : null;
    }

    /// <summary>An address as the crawler remembers it: no #part, so the same page isn't fetched twice.</summary>
    public static Uri Clean(Uri uri) => uri.Fragment.Length == 0 ? uri : new UriBuilder(uri) { Fragment = "" }.Uri;

    /// <summary>
    /// A link as written on a page, made into a whole address. Null for what isn't a web address at all: mail and
    /// phone links, script, and a bare #part of the same page.
    /// </summary>
    public static Uri? Resolve(Uri page, string? href, string? baseHref = null)
    {
        var link = (href ?? "").Trim();
        if (link.Length == 0 || link.StartsWith('#'))
            return null;
        var from = page;
        if (!string.IsNullOrWhiteSpace(baseHref) && Uri.TryCreate(page, baseHref.Trim(), out var based) && based.Scheme is "http" or "https")
            from = based;
        return Uri.TryCreate(from, link, out var whole) && whole.Scheme is "http" or "https" ? Clean(whole) : null;
    }

    /// <summary>The same site: the same domain, with or without www, whatever the page.</summary>
    public static bool SameSite(Uri start, Uri other) =>
        string.Equals(Bare(start.Host), Bare(other.Host), StringComparison.OrdinalIgnoreCase);

    private static string Bare(string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    /// <summary>Worth reading as a page, going by its ending; pictures, PDFs and the like are only checked.</summary>
    public static bool LooksLikePage(Uri uri) => !NotPages.Contains(Path.GetExtension(uri.AbsolutePath));

    /// <summary>The part of robots.txt meant for every crawler (User-agent: *).</summary>
    public static RobotsRules Robots(string? robotsTxt)
    {
        var disallowed = new List<string>();
        var allowed = new List<string>();
        var forEveryone = false;
        var inAgents = false;
        foreach (var raw in (robotsTxt ?? "").Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            if (key == "user-agent")
            {
                // Several User-agent lines in a row share the rules that follow them.
                forEveryone = inAgents ? forEveryone || value == "*" : value == "*";
                inAgents = true;
                continue;
            }
            inAgents = false;
            if (!forEveryone || value.Length == 0)
                continue;
            if (key == "disallow")
                disallowed.Add(value);
            else if (key == "allow")
                allowed.Add(value);
        }
        return new RobotsRules(disallowed, allowed);
    }

    /// <summary>What is wrong with a page, in plain words, most serious first. Empty when nothing is.</summary>
    public static IReadOnlyList<string> Problems(CrawledPage page, IReadOnlyList<CheckedLink> brokenLinks)
    {
        var problems = new List<string>();
        if (page.Status == 0)
            problems.Add("No answer: " + (page.Problem ?? "the server didn't reply") + ".");
        else if (page.Status >= 400)
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"The page itself is broken ({page.Status} {StatusWords(page.Status)})."));
        if (page.RedirectedTo is { } to)
            problems.Add($"Sends visitors on to {to}. Link straight to that address instead.");
        if (page.Facts is not { } facts || !page.Ok)
            return problems;

        if (brokenLinks.Count > 0)
            problems.Add(brokenLinks.Count == 1 ? $"1 broken link: {Said(brokenLinks[0])}." : $"{brokenLinks.Count} broken links, the first: {Said(brokenLinks[0])}.");
        if (facts.Title.Length == 0)
            problems.Add("No title. It is the heading Google shows for the page.");
        else if (facts.Title.Length > TitleLength)
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"The title is {facts.Title.Length} characters; Google cuts it off at about {TitleLength}."));
        if (facts.Description.Length == 0)
            problems.Add("No description. Google then picks its own words from the page.");
        else if (facts.Description.Length > DescriptionLength)
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"The description is {facts.Description.Length} characters; Google cuts it off at about {DescriptionLength}."));
        if (facts.Headings.Count == 0)
            problems.Add("No main heading (H1).");
        else if (facts.Headings.Count > 1)
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{facts.Headings.Count} main headings (H1); a page should have one."));
        if (facts.ImagesWithoutAlt.Count > 0)
            problems.Add(facts.ImagesWithoutAlt.Count == 1 ? "1 picture has no alt text." : string.Create(CultureInfo.InvariantCulture, $"{facts.ImagesWithoutAlt.Count} pictures have no alt text."));
        if (facts.NoIndex)
            problems.Add("Asks search engines not to list it (noindex). Right for a thank-you page, wrong for most others.");
        return problems;
    }

    /// <summary>The pages that are titled the same, which Google reads as the same page twice.</summary>
    public static IReadOnlySet<Uri> SameTitles(IEnumerable<CrawledPage> pages) =>
        pages.Where(p => p.Ok && p.RedirectedTo is null && p.Facts is { Title.Length: > 0 })
            .GroupBy(p => p.Facts!.Title, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(p => p.Url))
            .ToHashSet();

    /// <summary>One line on how the whole crawl went.</summary>
    public static string Summary(IReadOnlyList<CrawlRow> rows, bool stoppedAtLimit)
    {
        var withProblems = rows.Count(r => r.Problems.Count > 0);
        var broken = rows.SelectMany(r => r.BrokenLinks).Select(l => l.Url).Distinct().Count();
        var text = new StringBuilder(rows.Count == 1 ? "1 page" : string.Create(CultureInfo.InvariantCulture, $"{rows.Count} pages"));
        text.Append(withProblems == 0 ? ", nothing to fix" : string.Create(CultureInfo.InvariantCulture, $", {withProblems} with something to fix"));
        if (broken > 0)
            text.Append(broken == 1 ? ", 1 broken link" : string.Create(CultureInfo.InvariantCulture, $", {broken} broken links"));
        text.Append('.');
        if (stoppedAtLimit)
            text.Append(" Stopped at the limit; there are more pages.");
        return text.ToString();
    }

    /// <summary>
    /// The table as a CSV file a spreadsheet opens. The first line names the separator, so Excel splits the
    /// columns the same on a PC that uses commas for decimals.
    /// </summary>
    public static string Csv(IReadOnlyList<CrawlRow> rows)
    {
        var csv = new StringBuilder("sep=,\r\n");
        Line("Address", "Status", "Sends you on to", "Title", "Title length", "Description", "Description length", "Main headings (H1)", "Pictures",
            "Pictures without alt text", "Broken links", "Listed by search engines", "Found on", "To fix");
        foreach (var row in rows)
        {
            var page = row.Page;
            var facts = page.Facts;
            Line(
                page.Url.ToString(),
                page.Status == 0 ? "No answer" : page.Status.ToString(CultureInfo.InvariantCulture),
                page.RedirectedTo?.ToString() ?? "",
                facts?.Title ?? "",
                Number(facts?.Title.Length),
                facts?.Description ?? "",
                Number(facts?.Description.Length),
                facts is null ? "" : string.Join(" | ", facts.Headings),
                Number(facts?.Images.Count),
                Number(facts?.ImagesWithoutAlt.Count),
                string.Join(" | ", row.BrokenLinks.Select(Said)),
                facts is null ? "" : facts.NoIndex ? "No (noindex)" : "Yes",
                page.FoundOn?.ToString() ?? "",
                string.Join(" ", row.Problems));
        }
        return csv.ToString();

        void Line(params string[] cells) => csv.AppendJoin(',', cells.Select(Cell)).Append("\r\n");
    }

    /// <summary>A broken link with what it answered: "https://x.co.za/old (404)".</summary>
    public static string Said(CheckedLink link) =>
        link.Url + (link.Status == 0 ? " (no answer)" : string.Create(CultureInfo.InvariantCulture, $" ({link.Status})"));

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    // Quoted when it must be; a cell that starts like a formula gets a ' in front, so a spreadsheet shows it
    // rather than working it out (a page title is somebody else's text).
    private static string Cell(string text)
    {
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
            text = "'" + text;
        return text.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }

    /// <summary>The usual words for an answer's number.</summary>
    public static string StatusWords(int status) => status switch
    {
        0 => "no answer",
        200 => "fine",
        301 or 308 => "moved for good",
        302 or 303 or 307 => "moved for now",
        401 => "needs a sign-in",
        403 => "not allowed",
        404 => "not found",
        410 => "gone",
        429 => "too many requests",
        >= 500 => "server error",
        >= 400 => "refused",
        >= 300 => "moved",
        _ => "fine",
    };
}
