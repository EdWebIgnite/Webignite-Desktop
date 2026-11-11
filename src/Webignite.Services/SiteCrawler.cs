using System.Net;
using Webignite.Core.Web;

namespace Webignite.Services;

/// <summary>How far a crawl has got, told to the page as it goes.</summary>
public sealed record CrawlProgress(int PagesRead, int PagesWaiting, int LinksChecked, string Now);

/// <summary>What a crawl found.</summary>
public sealed record CrawlResult(IReadOnlyList<CrawlRow> Rows, bool StoppedAtLimit, bool Cancelled);

/// <summary>
/// Walks a website from one address: reads each page of the same site, follows its links, and checks that the
/// links, pictures, stylesheets and scripts it points at answer. Reads the HTML as the server sends it, running no
/// JavaScript, unless it is handed a browser to read each page with (for sites built by scripts). Polite: a few requests at a time, robots.txt kept to, and it says who it is. Read-only.
/// Tests hand in their own <see cref="HttpClient"/> over a fake handler.
/// </summary>
public sealed class SiteCrawler(HttpClient http)
{
    /// <summary>Redirects are followed by hand so each page can say where it sends visitors.</summary>
    public static SiteCrawler Default { get; } = new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All })
    {
        Timeout = TimeSpan.FromSeconds(20),
    });

    public const string Agent = "WebigniteSiteCrawler/1.0 (a site owner's own check)";

    /// <summary>How many requests are out at once: enough to be quick, few enough not to lean on a small host.</summary>
    public const int AtOnce = 4;

    /// <summary>A page bigger than this is not read past here; no real page is.</summary>
    public const int MostBytes = 3_000_000;

    private const int MostHops = 5;

    public Task<CrawlResult> CrawlAsync(Uri start, int mostPages, IProgress<CrawlProgress>? progress = null, CancellationToken cancel = default) =>
        CrawlAsync(start, mostPages, progress, null, cancel);

    /// <param name="rendered">
    /// For sites that build their pages with JavaScript: given a page's address, the HTML as a browser has it
    /// after the scripts ran, or null to use what the server sent. Pages are then read one at a time, as there
    /// is one browser. Links are still only checked for an answer, as before.
    /// </param>
    public async Task<CrawlResult> CrawlAsync(Uri start, int mostPages, IProgress<CrawlProgress>? progress,
        Func<Uri, CancellationToken, Task<string?>>? rendered, CancellationToken cancel = default)
    {
        mostPages = Math.Clamp(mostPages, 1, SiteCrawl.MostPages);
        var atOnce = rendered is null ? AtOnce : 1;
        var robots = await RobotsAsync(start, cancel);
        var pages = new List<CrawledPage>();
        var seen = new HashSet<Uri> { start };
        var waiting = new Queue<(Uri Url, Uri? FoundOn)>();
        waiting.Enqueue((start, null));
        var stoppedAtLimit = false;
        var cancelled = false;

        // Pages, a few at a time; each one's links are added to the queue as it comes back.
        var running = new List<Task<CrawledPage>>();
        try
        {
            while (waiting.Count > 0 || running.Count > 0)
            {
                while (waiting.Count > 0 && running.Count < atOnce && pages.Count + running.Count < mostPages)
                {
                    var (url, foundOn) = waiting.Dequeue();
                    running.Add(PageAsync(url, foundOn, rendered, cancel));
                }
                if (running.Count == 0)
                {
                    stoppedAtLimit = waiting.Count > 0;
                    break;
                }
                var done = await Task.WhenAny(running);
                running.Remove(done);
                var page = await done;
                pages.Add(page);
                progress?.Report(new CrawlProgress(pages.Count, waiting.Count + running.Count, 0, page.Url.ToString()));
                if (page.RedirectedTo is { } movedTo)
                    Follow(movedTo, page.Url);
                if (page.Facts is null)
                    continue;
                foreach (var link in page.Facts.Links)
                    if (!link.NoFollow && SiteCrawl.Resolve(page.Url, link.Href, page.Facts.Base) is { } target)
                        Follow(target, page.Url);
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        void Follow(Uri target, Uri from)
        {
            if (SiteCrawl.SameSite(start, target) && SiteCrawl.LooksLikePage(target) && robots.Allows(target.PathAndQuery) && seen.Add(target))
                waiting.Enqueue((target, from));
        }

        // Everything the pages point at that wasn't read as a page: does it answer?
        var read = pages.ToDictionary(p => p.Url);
        var pointedAt = new Dictionary<Uri, List<Uri>>();
        foreach (var page in pages.Where(p => p.Facts is not null))
        {
            var from = page.Url;
            var facts = page.Facts!;
            // Links the page asks not to be followed, and anything robots.txt keeps crawlers out of, are left alone.
            foreach (var href in facts.Links.Where(l => !l.NoFollow).Select(l => l.Href).Concat(facts.Images).Concat(facts.Assets))
            {
                if (SiteCrawl.Resolve(from, href, facts.Base) is not { } target || (SiteCrawl.SameSite(start, target) && !robots.Allows(target.PathAndQuery)))
                    continue;
                if (!pointedAt.TryGetValue(target, out var by))
                    pointedAt[target] = by = [];
                if (!by.Contains(page.Url))
                    by.Add(page.Url);
            }
        }
        var toCheck = pointedAt.Keys.Where(u => !read.ContainsKey(u)).ToList();
        var checkedLinks = new Dictionary<Uri, CheckedLink>();
        if (!cancelled)
        {
            try
            {
                // These go to many different servers, so more can be out at once than to the site itself.
                using var gate = new SemaphoreSlim(AtOnce * 2);
                var count = 0;
                var checks = toCheck.Select(async url =>
                {
                    await gate.WaitAsync(cancel);
                    try
                    {
                        var result = await CheckAsync(url, cancel);
                        progress?.Report(new CrawlProgress(pages.Count, 0, Interlocked.Increment(ref count), url.ToString()));
                        return result;
                    }
                    finally
                    {
                        gate.Release();
                    }
                }).ToList();
                foreach (var result in await Task.WhenAll(checks))
                    checkedLinks[result.Url] = result;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
        }

        var sameTitles = SiteCrawl.SameTitles(pages);
        var rows = pages.Select(page =>
        {
            var broken = new List<CheckedLink>();
            foreach (var (target, by) in pointedAt)
            {
                if (!by.Contains(page.Url))
                    continue;
                if (checkedLinks.TryGetValue(target, out var link) && link.Broken)
                    broken.Add(link);
                else if (read.TryGetValue(target, out var other) && other.Status is 0 or >= 400)
                    broken.Add(new CheckedLink(target, other.Status, other.Problem));
            }
            broken.Sort((a, b) => string.CompareOrdinal(a.Url.ToString(), b.Url.ToString()));
            var problems = SiteCrawl.Problems(page, broken).ToList();
            if (sameTitles.Contains(page.Url))
                problems.Add("Another page has the same title.");
            return new CrawlRow(page, problems, broken);
        }).ToList();
        return new CrawlResult(rows, stoppedAtLimit, cancelled);
    }

    private async Task<RobotsRules> RobotsAsync(Uri start, CancellationToken cancel)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, new Uri(start, "/robots.txt"), cancel);
            return response.IsSuccessStatusCode ? SiteCrawl.Robots(await response.Content.ReadAsStringAsync(cancel)) : RobotsRules.None;
        }
        catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            return RobotsRules.None;
        }
    }

    private async Task<CrawledPage> PageAsync(Uri url, Uri? foundOn, Func<Uri, CancellationToken, Task<string?>>? rendered, CancellationToken cancel)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, url, cancel);
            var status = (int)response.StatusCode;
            // A page that sends visitors elsewhere is a row of its own; where it sends them is read as its own page.
            if (status is >= 300 and < 400 && response.Headers.Location is { } location)
                return new CrawledPage { Url = url, Status = status, RedirectedTo = SiteCrawl.Clean(new Uri(url, location)), FoundOn = foundOn };
            var isHtml = response.Content.Headers.ContentType?.MediaType is null or "text/html" or "application/xhtml+xml";
            PageFacts? facts = null;
            if (response.IsSuccessStatusCode && isHtml)
            {
                // The server's answer says whether there is a page; the browser, when there is one, says what is on it.
                var sent = await ReadAsync(response, cancel);
                facts = HtmlFacts.Read(rendered is null ? sent : await rendered(url, cancel) ?? sent);
            }
            return new CrawledPage { Url = url, Status = status, Facts = facts, FoundOn = foundOn };
        }
        catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            return new CrawledPage { Url = url, Status = 0, Problem = Why(e), FoundOn = foundOn };
        }
    }

    // Only whether it answers: HEAD first, as it sends nothing back, and GET when HEAD says no.
    private async Task<CheckedLink> CheckAsync(Uri url, CancellationToken cancel)
    {
        try
        {
            foreach (var method in new[] { HttpMethod.Head, HttpMethod.Get })
            {
                var at = url;
                for (var hop = 0; ; hop++)
                {
                    using var response = await SendAsync(method, at, cancel);
                    var status = (int)response.StatusCode;
                    if (status is >= 300 and < 400 && response.Headers.Location is { } location && hop < MostHops)
                    {
                        at = new Uri(at, location);
                        continue;
                    }
                    // Some servers answer HEAD with an error for a page that is there (Google's help pages say 404),
                    // so a HEAD that fails is asked again properly before anything is called broken.
                    if (method == HttpMethod.Head && status >= 400)
                        break;
                    // Sites that turn away anything that isn't a browser aren't broken for a visitor.
                    return new CheckedLink(url, status is 401 or 403 or 429 or 999 ? 200 : status, null);
                }
            }
            return new CheckedLink(url, 200, null);
        }
        catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            return new CheckedLink(url, 0, Why(e));
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri url, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd(Agent);
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,*/*;q=0.8");
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken cancel)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var kept = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while (kept.Length < MostBytes && (read = await stream.ReadAsync(buffer, cancel)) > 0)
            kept.Write(buffer, 0, read);
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        System.Text.Encoding encoding;
        try
        {
            encoding = string.IsNullOrEmpty(charset) ? System.Text.Encoding.UTF8 : System.Text.Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            encoding = System.Text.Encoding.UTF8;
        }
        return encoding.GetString(kept.GetBuffer(), 0, (int)kept.Length);
    }

    private static string Why(Exception e) => e switch
    {
        TaskCanceledException => "it took too long to answer",
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "there is no such server",
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => "its security certificate isn't right",
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } => "the server couldn't be reached",
        _ => "the server didn't reply properly",
    };
}
