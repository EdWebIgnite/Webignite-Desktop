using System.Net;
using System.Text;
using Webignite.Core.Web;
using Webignite.Services;

namespace Webignite.Tests;

public sealed class SiteCrawlTests
{
    [Fact]
    public void A_page_gives_up_its_title_description_headings_links_and_pictures()
    {
        const string html = """
            <!DOCTYPE html>
            <html><head>
              <title>  Karoo Coffee &amp; Bakery
                 | Menu </title>
              <META NAME="Description" CONTENT="Fresh roasts &amp; rusks, baked daily.">
              <meta name=robots content="noindex, follow">
              <link rel="canonical" href="https://karoocoffee.co.za/menu">
              <link rel="stylesheet" href="/site.css">
              <base href="/shop/">
              <script src="/app.js"></script>
              <script>var s = "<a href='/not-a-link'>x</a><h1>no</h1>";</script>
              <style>a > b { content: "<img src=x>"; }</style>
            </head><body>
              <!-- <a href="/commented-out">old</a> <h1>Old</h1> -->
              <h1>Our <em>menu</em></h1>
              <a href="/about" title="a > b">About   us</a>
              <a href='contact.html' rel="nofollow noopener">Contact</a>
              <a href=mailto:hi@karoocoffee.co.za>Mail</a>
              <a href="/photo"><img src="/img/cup.jpg" alt="A cup"></a>
              <img src="/img/logo.png">
              <img src="/img/line.png" alt="">
              <img src="data:image/png;base64,AAAA">
              <IMG data-src="/img/lazy.webp" class=lazy>
              <h1>Second heading</h1>
            </body></html>
            """;
        var facts = HtmlFacts.Read(html);
        Assert.Equal("Karoo Coffee & Bakery | Menu", facts.Title);
        Assert.Equal("Fresh roasts & rusks, baked daily.", facts.Description);
        Assert.True(facts.NoIndex);
        Assert.Equal("https://karoocoffee.co.za/menu", facts.Canonical);
        Assert.Equal("/shop/", facts.Base);
        Assert.Equal(["Our menu", "Second heading"], facts.Headings);
        Assert.Equal(
            [new("/about", "About us", false), new("contact.html", "Contact", true), new("mailto:hi@karoocoffee.co.za", "Mail", false), new("/photo", "A cup", false)],
            facts.Links);
        Assert.Equal(["/img/cup.jpg", "/img/logo.png", "/img/line.png", "/img/lazy.webp"], facts.Images);
        Assert.Equal(["/img/logo.png", "/img/lazy.webp"], facts.ImagesWithoutAlt);
        Assert.Equal(["/site.css", "/app.js"], facts.Assets);
    }

    [Fact]
    public void Broken_html_is_read_as_far_as_it_goes()
    {
        Assert.Equal("", HtmlFacts.Read(null).Title);
        Assert.Equal("Cut off", HtmlFacts.Read("<title>Cut off").Title);
        Assert.Equal([new PageLink("/x", "never closed", false)], HtmlFacts.Read("<a href='/x'>never closed").Links);
        Assert.Equal([new PageLink("/a", "one", false), new PageLink("/b", "two", false)], HtmlFacts.Read("<a href=/a>one<a href=/b>two</a>").Links);
        Assert.Empty(HtmlFacts.Read("< not a tag > 3 < 4 <a>no href</a> <a href=''>empty</a>").Links);
        Assert.Empty(HtmlFacts.Read("<script>if (a < b) { x = '<a href=\"/no\">' }").Links);
        Assert.False(HtmlFacts.Read("<meta name='robots' content='index,follow'>").NoIndex);
        Assert.Empty(HtmlFacts.Read("<img src='/deco.png' role='presentation'><img src='/d2.png' aria-hidden='true'>").ImagesWithoutAlt);
        Assert.Equal("First", HtmlFacts.Read("<title>First</title><svg><title>Icon</title></svg>").Title);
    }

    [Theory]
    [InlineData("karoocoffee.co.za", "https://karoocoffee.co.za/")]
    [InlineData(" http://www.Karoo.co.za/menu#top ", "http://www.karoo.co.za/menu")]
    [InlineData("https://karoo.co.za/a?b=1", "https://karoo.co.za/a?b=1")]
    public void A_crawl_starts_from_what_was_typed(string typed, string expected) => Assert.Equal(expected, SiteCrawl.Start(typed)!.ToString());

    [Fact]
    public void Addresses_are_made_whole_and_sorted_into_site_and_not()
    {
        Assert.Null(SiteCrawl.Start(""));
        Assert.Null(SiteCrawl.Start("two words"));
        Assert.Null(SiteCrawl.Start("ftp://karoo.co.za"));
        Assert.Null(SiteCrawl.Start("localhost"));

        var page = new Uri("https://karoo.co.za/menu/drinks");
        Assert.Equal("https://karoo.co.za/about", SiteCrawl.Resolve(page, "/about")!.ToString());
        Assert.Equal("https://karoo.co.za/menu/food", SiteCrawl.Resolve(page, "food#mains")!.ToString());
        Assert.Equal("https://karoo.co.za/shop/tea", SiteCrawl.Resolve(page, "tea", "/shop/")!.ToString());
        Assert.Equal("https://other.co.za/", SiteCrawl.Resolve(page, "//other.co.za")!.ToString());
        Assert.Null(SiteCrawl.Resolve(page, "#top"));
        Assert.Null(SiteCrawl.Resolve(page, "mailto:hi@karoo.co.za"));
        Assert.Null(SiteCrawl.Resolve(page, "tel:+27825550142"));
        Assert.Null(SiteCrawl.Resolve(page, "javascript:void(0)"));
        Assert.Null(SiteCrawl.Resolve(page, "  "));

        Assert.True(SiteCrawl.SameSite(page, new Uri("http://WWW.karoo.co.za/x")));
        Assert.False(SiteCrawl.SameSite(page, new Uri("https://shop.karoo.co.za/")));
        Assert.True(SiteCrawl.LooksLikePage(new Uri("https://karoo.co.za/menu.html")));
        Assert.True(SiteCrawl.LooksLikePage(new Uri("https://karoo.co.za/menu/")));
        Assert.False(SiteCrawl.LooksLikePage(new Uri("https://karoo.co.za/menu.PDF?v=2")));
        Assert.False(SiteCrawl.LooksLikePage(new Uri("https://karoo.co.za/img/cup.webp")));
    }

    [Fact]
    public void Robots_rules_for_everyone_are_kept_to()
    {
        var rules = SiteCrawl.Robots("""
            # Keep out of the back office
            User-agent: Googlebot
            Disallow: /only-google

            User-agent: bingbot
            User-agent: *
            Disallow: /wp-admin/
            Allow: /wp-admin/admin-ajax.php
            Disallow: /*.pdf$
            Disallow: /search
            Disallow:
            Sitemap: https://karoo.co.za/sitemap.xml
            """);
        Assert.True(rules.Allows("/"));
        Assert.True(rules.Allows("/only-google"));
        Assert.False(rules.Allows("/wp-admin/options.php"));
        Assert.True(rules.Allows("/wp-admin/admin-ajax.php"));
        Assert.False(rules.Allows("/files/menu.pdf"));
        Assert.True(rules.Allows("/files/menu.pdf?download=1"));
        Assert.False(rules.Allows("/search?q=tea"));
        Assert.True(RobotsRules.None.Allows("/anything"));
        Assert.True(SiteCrawl.Robots(null).Allows("/x"));
        Assert.False(SiteCrawl.Robots("User-agent: *\nDisallow: /").Allows("/x"));
    }

    private static CrawledPage Page(string url, PageFacts? facts = null, int status = 200, string? to = null) =>
        new() { Url = new Uri(url), Status = status, Facts = facts, RedirectedTo = to is null ? null : new Uri(to) };

    [Fact]
    public void A_good_page_has_nothing_to_fix()
    {
        var facts = new PageFacts { Title = "Karoo Coffee: menu", Description = "Fresh roasts and rusks, baked daily in Graaff-Reinet.", Headings = ["Menu"], Images = ["/a.jpg"] };
        Assert.Empty(SiteCrawl.Problems(Page("https://karoo.co.za/menu", facts), []));
    }

    [Fact]
    public void Each_problem_is_said_in_plain_words()
    {
        var facts = new PageFacts
        {
            Title = new string('t', 71), Description = new string('d', 181), Headings = ["One", "Two"], ImagesWithoutAlt = ["/a.jpg", "/b.jpg"], NoIndex = true,
        };
        var broken = new[] { new CheckedLink(new Uri("https://karoo.co.za/old"), 404, null), new CheckedLink(new Uri("https://gone.example/"), 0, "there is no such server") };
        Assert.Equal(
        [
            "2 broken links, the first: https://karoo.co.za/old (404).",
            "The title is 71 characters; Google cuts it off at about 60.",
            "The description is 181 characters; Google cuts it off at about 160.",
            "2 main headings (H1); a page should have one.",
            "2 pictures have no alt text.",
            "Asks search engines not to list it (noindex). Right for a thank-you page, wrong for most others.",
        ], SiteCrawl.Problems(Page("https://karoo.co.za/", facts), broken));

        Assert.Equal(
            ["No title. It is the heading Google shows for the page.", "No description. Google then picks its own words from the page.", "No main heading (H1)."],
            SiteCrawl.Problems(Page("https://karoo.co.za/", new PageFacts()), []));
        Assert.Equal(["The page itself is broken (404 not found)."], SiteCrawl.Problems(Page("https://karoo.co.za/x", status: 404), []));
        Assert.Equal(["No answer: it took too long to answer."],
            SiteCrawl.Problems(new CrawledPage { Url = new Uri("https://karoo.co.za/x"), Problem = "it took too long to answer" }, []));
        Assert.StartsWith("Sends visitors on to https://karoo.co.za/new.",
            SiteCrawl.Problems(Page("https://karoo.co.za/old", new PageFacts { Title = "T", Description = "D", Headings = ["H"] }, to: "https://karoo.co.za/new"), [])[0]);
    }

    [Fact]
    public void Pages_with_the_same_title_are_found()
    {
        var same = SiteCrawl.SameTitles(
        [
            Page("https://karoo.co.za/", new PageFacts { Title = "Karoo Coffee" }),
            Page("https://karoo.co.za/menu", new PageFacts { Title = "karoo coffee" }),
            Page("https://karoo.co.za/about", new PageFacts { Title = "About" }),
            Page("https://karoo.co.za/gone", new PageFacts { Title = "Karoo Coffee" }, status: 404),
            Page("https://karoo.co.za/blank", new PageFacts()),
            Page("https://karoo.co.za/blank2", new PageFacts()),
        ]);
        Assert.Equal(2, same.Count);
        Assert.Contains(new Uri("https://karoo.co.za/menu"), same);
    }

    [Fact]
    public void The_summary_and_the_spreadsheet()
    {
        var ok = new CrawlRow(Page("https://karoo.co.za/", new PageFacts { Title = "Karoo, \"best\" coffee", Description = "D", Headings = ["H"], Images = ["/a.jpg"] }), [], []);
        var bad = new CrawlRow(
            Page("https://karoo.co.za/menu", new PageFacts { Title = "=SUM(A1)", Headings = ["A", "B"] }) with { FoundOn = new Uri("https://karoo.co.za/") },
            ["No description.", "2 main headings."], [new CheckedLink(new Uri("https://karoo.co.za/old"), 404, null), new CheckedLink(new Uri("https://gone.example/"), 0, "x")]);
        Assert.Equal("2 pages, 1 with something to fix, 2 broken links.", SiteCrawl.Summary([ok, bad], false));
        Assert.Equal("1 page, nothing to fix. Stopped at the limit; there are more pages.", SiteCrawl.Summary([ok], true));

        var lines = SiteCrawl.Csv([ok, bad]).Split("\r\n");
        Assert.Equal("sep=,", lines[0]);
        Assert.StartsWith("Address,Status,Sends you on to,Title,", lines[1]);
        Assert.Equal("https://karoo.co.za/,200,,\"Karoo, \"\"best\"\" coffee\",20,D,1,H,1,0,,Yes,,", lines[2]);
        Assert.Equal(
            "https://karoo.co.za/menu,200,,'=SUM(A1),8,,0,A | B,0,0,https://karoo.co.za/old (404) | https://gone.example/ (no answer),Yes,https://karoo.co.za/,No description. 2 main headings.",
            lines[3]);
        Assert.Equal("", lines[4]);
    }

    // A pretend website: each address answers with a page, a status, or a move elsewhere.
    private sealed class FakeSite(Dictionary<string, (int Status, string Body, string? Location)> pages) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var url = request.RequestUri!.ToString();
            lock (Asked)
                Asked.Add(request.Method + " " + url);
            // A server that says 404 to HEAD for a page that is there, as some do.
            if (request.RequestUri.Host == "picky.example" && request.Method == HttpMethod.Head)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (!pages.TryGetValue(url, out var page))
            {
                return request.RequestUri.Host == "gone.example"
                    ? Task.FromException<HttpResponseMessage>(new HttpRequestException(HttpRequestError.NameResolutionError))
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            var response = new HttpResponseMessage((HttpStatusCode)page.Status);
            if (page.Location is not null)
                response.Headers.Location = new Uri(page.Location, UriKind.RelativeOrAbsolute);
            else
                response.Content = new StringContent(page.Body, Encoding.UTF8, url.EndsWith(".jpg", StringComparison.Ordinal) ? "image/jpeg" : url.EndsWith(".txt", StringComparison.Ordinal) ? "text/plain" : "text/html");
            return Task.FromResult(response);
        }
    }

    private static string Html(string title, string body) => $"<title>{title}</title><meta name='description' content='About {title}'><h1>{title}</h1>{body}";

    [Fact]
    public async Task A_site_built_by_scripts_is_read_as_the_browser_has_it()
    {
        // The server sends an empty shell; the menu link and the titles only exist once the scripts have run.
        var site = new FakeSite(new()
        {
            ["https://app.co.za/"] = (200, "<title>Loading</title><div id='root'></div>", null),
            ["https://app.co.za/menu"] = (200, "<title>Loading</title><div id='root'></div>", null),
            ["https://app.co.za/plain"] = (200, Html("Plain", ""), null),
        });
        var asked = new List<string>();
        Task<string?> Browser(Uri address, CancellationToken cancel)
        {
            asked.Add(address.AbsolutePath);
            return Task.FromResult(address.AbsolutePath switch
            {
                "/" => Html("Home", "<a href='/menu'>Menu</a> <a href='/plain'>Plain</a>"),
                "/menu" => Html("Menu", "<a href='/'>Home</a>"),
                _ => null,
            });
        }

        var result = await new SiteCrawler(new HttpClient(site)).CrawlAsync(new Uri("https://app.co.za/"), 50, null, Browser, TestContext.Current.CancellationToken);

        var rows = result.Rows.ToDictionary(r => r.Page.Url.AbsolutePath);
        Assert.Equal(["/", "/menu", "/plain"], rows.Keys.Order());
        Assert.Equal("Home", rows["/"].Page.Facts!.Title);
        Assert.Equal("Menu", rows["/menu"].Page.Facts!.Title);
        // When the browser gets nothing from a page, what the server sent is used.
        Assert.Equal("Plain", rows["/plain"].Page.Facts!.Title);
        Assert.Equal(["/", "/menu", "/plain"], asked.Order());
    }

    [Fact]
    public async Task Without_a_browser_a_site_built_by_scripts_shows_only_its_shell()
    {
        var site = new FakeSite(new() { ["https://app.co.za/"] = (200, "<title>Loading</title><div id='root'></div>", null) });
        var result = await new SiteCrawler(new HttpClient(site)).CrawlAsync(new Uri("https://app.co.za/"), 50, null, TestContext.Current.CancellationToken);
        Assert.Equal("Loading", Assert.Single(result.Rows).Page.Facts!.Title);
    }

    [Fact]
    public void The_browsers_answer_is_read_for_its_html()
    {
        Assert.Equal("<html><body>Hi</body></html>", RenderedHtml.Read("""{"result":{"type":"string","value":"<html><body>Hi</body></html>"}}"""));
        Assert.Null(RenderedHtml.Read("""{"result":{"type":"undefined"}}"""));
        Assert.Null(RenderedHtml.Read("""{"result":{"type":"string","value":""}}"""));
        Assert.Null(RenderedHtml.Read("not json"));
        Assert.Null(RenderedHtml.Read(null));
        Assert.Contains("\"awaitPromise\":true", RenderedHtml.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_crawl_walks_the_site_keeps_to_robots_and_finds_what_is_broken()
    {
        var site = new FakeSite(new()
        {
            ["https://karoo.co.za/robots.txt"] = (200, "User-agent: *\nDisallow: /private", null),
            ["https://karoo.co.za/"] = (200, Html("Home", "<a href='/menu'>Menu</a> <a href='/old'>Old</a> <a href='/private/x'>Private</a> <a href='https://partner.example/'>Partner</a> <a href='https://picky.example/help'>Picky</a>"
                + "<a href='https://gone.example/'>Gone</a> <a href='/menu#top'>Menu again</a> <a href='/moved'>Moved</a> <img src='/cup.jpg' alt='Cup'> <img src='/missing.jpg' alt='x'>"), null),
            ["https://karoo.co.za/menu"] = (200, Html("Menu", "<a href='/'>Home</a> <a href='/menu/tea' rel='nofollow'>Tea</a> <a href='/brochure.pdf'>Brochure</a>"), null),
            ["https://karoo.co.za/moved"] = (301, "", "/menu"),
            ["https://karoo.co.za/cup.jpg"] = (200, "jpg", null),
            ["https://karoo.co.za/brochure.pdf"] = (200, "pdf", null),
            ["https://partner.example/"] = (403, "Go away, robot", null),
            ["https://picky.example/help"] = (200, "Here when asked properly", null),
        });
        var progress = new List<CrawlProgress>();
        var result = await new SiteCrawler(new HttpClient(site)).CrawlAsync(new Uri("https://karoo.co.za/"), 50, new Report(progress), TestContext.Current.CancellationToken);

        Assert.False(result.StoppedAtLimit);
        Assert.False(result.Cancelled);
        var rows = result.Rows.ToDictionary(r => r.Page.Url.AbsolutePath);
        // The private page (robots), the nofollow link and the PDF are never read as pages; the menu is read once.
        Assert.Equal(["/", "/menu", "/moved", "/old"], rows.Keys.Order());
        Assert.DoesNotContain(site.Asked, a => a.Contains("/private", StringComparison.Ordinal) || a.Contains("/menu/tea", StringComparison.Ordinal));
        Assert.Single(site.Asked, a => a == "GET https://karoo.co.za/menu");

        Assert.Equal(["https://gone.example/", "https://karoo.co.za/missing.jpg", "https://karoo.co.za/old"], rows["/"].BrokenLinks.Select(l => l.Url.ToString()));
        Assert.Equal("3 broken links, the first: https://gone.example/ (no answer).", rows["/"].Problems[0]);
        Assert.Empty(rows["/menu"].Problems);
        Assert.Equal("The page itself is broken (404 not found).", Assert.Single(rows["/old"].Problems));
        Assert.Equal("/", rows["/old"].Page.FoundOn!.AbsolutePath);
        Assert.Equal("https://karoo.co.za/menu", rows["/moved"].Page.RedirectedTo!.ToString());
        Assert.Contains(rows["/moved"].Problems, p => p.StartsWith("Sends visitors on to https://karoo.co.za/menu.", StringComparison.Ordinal));
        // A site that turns robots away isn't broken for a visitor, and files are only asked whether they are there.
        Assert.DoesNotContain(rows["/"].BrokenLinks, l => l.Url.Host == "partner.example");
        Assert.DoesNotContain(rows["/"].BrokenLinks, l => l.Url.Host == "picky.example");
        Assert.Contains("GET https://picky.example/help", site.Asked);
        Assert.Contains("HEAD https://karoo.co.za/brochure.pdf", site.Asked);
        Assert.DoesNotContain("GET https://karoo.co.za/brochure.pdf", site.Asked);
        Assert.Equal("4 pages, 3 with something to fix, 3 broken links.", SiteCrawl.Summary(result.Rows, result.StoppedAtLimit));
        Assert.Equal(4, progress.Max(p => p.PagesRead));
    }

    [Fact]
    public async Task A_crawl_stops_at_its_limit_and_when_told_to()
    {
        var pages = Enumerable.Range(1, 30).ToDictionary(i => $"https://big.co.za/p{i}", i => (200, Html("Page " + i, $"<a href='/p{i + 1}'>Next</a>"), (string?)null));
        pages["https://big.co.za/"] = (200, Html("Home", "<a href='/p1'>First</a>"), null);
        var limited = await new SiteCrawler(new HttpClient(new FakeSite(pages))).CrawlAsync(new Uri("https://big.co.za/"), 5, null, TestContext.Current.CancellationToken);
        Assert.Equal(5, limited.Rows.Count);
        Assert.True(limited.StoppedAtLimit);

        using var stop = new CancellationTokenSource();
        var told = await new SiteCrawler(new HttpClient(new FakeSite(pages))).CrawlAsync(new Uri("https://big.co.za/"), 100, new Report([], seen => { if (seen.PagesRead == 3) stop.Cancel(); }), stop.Token);
        Assert.True(told.Cancelled);
        Assert.InRange(told.Rows.Count, 3, 10);
    }

    // Progress straight away, on the caller's thread, so a test can count on it.
    private sealed class Report(List<CrawlProgress> seen, Action<CrawlProgress>? then = null) : IProgress<CrawlProgress>
    {
        void IProgress<CrawlProgress>.Report(CrawlProgress value)
        {
            lock (seen)
                seen.Add(value);
            then?.Invoke(value);
        }
    }
}
