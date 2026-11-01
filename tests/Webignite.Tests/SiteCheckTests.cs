using Webignite.Core.Web;

namespace Webignite.Tests;

public sealed class SiteCheckTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    [Theory]
    [InlineData("https://www.Karoo.co.za/menu?x=1", "karoo.co.za")]
    [InlineData("karoo.co.za.", "karoo.co.za")]
    [InlineData("hello@fynbosphysio.co.za", "fynbosphysio.co.za")]
    [InlineData("shop.karoo.co.za:8080", "shop.karoo.co.za")]
    public void Domains_are_tidied(string input, string expected) => Assert.Equal(expected, DomainReport.Normalise(input));

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("bad_name.co.za")]
    [InlineData("-x.co.za")]
    public void Non_domains_are_refused(string input) => Assert.Null(DomainReport.Normalise(input));

    [Fact]
    public void Dns_answers_are_read()
    {
        const string json = """
            {"Status":0,"Answer":[
              {"name":"karoo.co.za.","type":16,"TTL":300,"data":"\"v=spf1 include:spf.host-h.net \" \"~all\""},
              {"name":"karoo.co.za.","type":15,"TTL":300,"data":"10 mx1.host-h.net."},
              {"name":"karoo.co.za.","type":5,"TTL":300,"data":"other.co.za."}]}
            """;
        Assert.Equal(["v=spf1 include:spf.host-h.net ~all"], DomainReport.DnsAnswers(json, 16));
        Assert.Equal(["10 mx1.host-h.net"], DomainReport.DnsAnswers(json, 15));
        Assert.Empty(DomainReport.DnsAnswers("""{"Status":3}""", 1));
    }

    private static DomainLookup Healthy => new()
    {
        Domain = "karoo.co.za",
        Today = Today,
        Addresses = ["41.203.18.10"],
        NameServers = ["ns2.dns-h.com", "ns1.host-h.net"],
        MailServers = ["20 mx2.host-h.net", "10 mx1.host-h.net"],
        Txt = ["v=spf1 include:spf.host-h.net ~all", "google-site-verification=abc"],
        DmarcTxt = ["v=DMARC1; p=quarantine; rua=mailto:dmarc@karoo.co.za"],
        CertificateExpires = new DateTimeOffset(2027, 1, 20, 0, 0, 0, TimeSpan.Zero),
        CertificateIssuer = "Let's Encrypt",
        RdapJson = """
            {"events":[{"eventAction":"registration","eventDate":"2019-03-02T00:00:00Z"},{"eventAction":"expiration","eventDate":"2027-03-02T00:00:00Z"}],
             "entities":[{"roles":["registrar"],"vcardArray":["vcard",[["version",{},"text","4.0"],["fn",{},"text","xneelo (Pty) Ltd"]]]}]}
            """,
    };

    [Fact]
    public void A_healthy_domain_has_no_problems()
    {
        var findings = DomainReport.Build(Healthy);
        Assert.DoesNotContain(findings, f => f.Problem);
        Assert.Equal("Valid from Let's Encrypt until 20 January 2027 (80 days left).", findings.Single(f => f.Label == "Security certificate").Value);
        Assert.Equal("Registered through xneelo (Pty) Ltd until 2 March 2027 (121 days left).", findings.Single(f => f.Label == "Registration").Value);
        Assert.Equal("Goes to mx1.host-h.net, mx2.host-h.net (xneelo)", findings.Single(f => f.Label == "Mail").Value);
    }

    [Fact]
    public void Trouble_shows_in_red()
    {
        var findings = DomainReport.Build(Healthy with
        {
            Txt = ["v=spf1 a ~all", "v=spf1 mx ~all"],
            DmarcTxt = [],
            CertificateExpires = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero),
            RdapJson = """{"events":[{"eventAction":"expiration","eventDate":"2026-11-20T00:00:00Z"}]}""",
        });
        Assert.All(findings.Where(f => f.Label is "SPF" or "DMARC" or "Security certificate" or "Registration"), f => Assert.True(f.Problem, f.Label));
        Assert.StartsWith("Expired 2 days ago", findings.Single(f => f.Label == "Security certificate").Value);
    }

    [Fact]
    public void Spf_and_dmarc_rules()
    {
        Assert.True(DomainReport.MailAuth(["v=spf1 +all"], ["v=DMARC1; p=reject"], true)[0].Problem);
        Assert.False(DomainReport.MailAuth([], [], hasMail: false)[0].Problem);
        var none = DomainReport.MailAuth(["v=spf1 mx ~all"], ["v=DMARC1; p=none"], true)[1];
        Assert.False(none.Problem);
        Assert.Contains("Watching only", none.Value);
        Assert.True(DomainReport.MailAuth(["v=spf1 mx ~all"], ["v=DMARC1; rua=mailto:x@y.z"], true)[1].Problem);
    }

    [Fact]
    public void Missing_registration_data_is_not_a_problem()
    {
        var finding = DomainReport.Build(Healthy with { RdapJson = null }).Single(f => f.Label == "Registration");
        Assert.False(finding.Problem);
    }

    [Fact]
    public void Pagespeed_results_are_read()
    {
        const string json = """
            {"lighthouseResult":{"categories":{"performance":{"score":0.42}},"audits":{
              "largest-contentful-paint":{"displayValue":"6.1 s","numericValue":6100},
              "first-contentful-paint":{"displayValue":"1.2 s","numericValue":1200},
              "total-blocking-time":{"displayValue":"350 ms","numericValue":350},
              "cumulative-layout-shift":{"displayValue":"0.02","numericValue":0.02},
              "speed-index":{"displayValue":"4.0 s","numericValue":4000},
              "modern-image-formats":{"title":"Serve images in modern formats","details":{"type":"opportunity","overallSavingsMs":2150}},
              "render-blocking-resources":{"title":"Eliminate render-blocking resources","details":{"type":"opportunity","overallSavingsMs":640}},
              "unused-css-rules":{"title":"Reduce unused CSS","details":{"type":"opportunity","overallSavingsMs":40}},
              "server-response-time":{"title":"Reduce server response time","details":{"type":"debugdata"}}}}}
            """;
        var report = SpeedReport.Parse(json);
        Assert.Equal(42, report.Score);
        Assert.Equal("Slow", report.Verdict);
        Assert.Equal(["Main content shows", "First thing shows", "Page frozen for", "Things jumping about", "Looks loaded"], report.Metrics.Select(m => m.Label));
        Assert.Equal([true, false, true, false, true], report.Metrics.Select(m => m.Problem));
        Assert.Equal(["Serve images in modern formats (saves about 2.2 s)", "Eliminate render-blocking resources (saves about 640 ms)"], report.Fixes);
    }

    [Theory]
    [InlineData("karoo.co.za", "https://karoo.co.za/")]
    [InlineData("http://karoo.co.za/menu", "http://karoo.co.za/menu")]
    [InlineData("not a site", null)]
    public void Speed_addresses(string input, string? expected) => Assert.Equal(expected, SpeedReport.Address(input));
}
