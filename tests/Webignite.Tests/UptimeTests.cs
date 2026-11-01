using Webignite.Core.Clients;
using Webignite.Core.Overview;
using Webignite.Core.Web;

namespace Webignite.Tests;

public sealed class UptimeTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);
    private static readonly DateTimeOffset Nine = new(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(2));

    private static readonly Client[] Clients =
    [
        new() { Id = "1", Name = "Karoo Coffee Roasters", Website = "https://www.karoocoffee.co.za/" },
        new() { Id = "2", Name = "Bosveld Builders", Website = "bosveldbuilders.co.za" },
        new() { Id = "3", Name = "No site" },
        new() { Id = "4", Name = "Karoo Coffee (shop)", Website = "karoocoffee.co.za/shop" },
    ];

    private static UptimePing Up(DateTimeOffset at, int ms = 400, int certificateDays = 60) => new(at, true, 200, ms, at.AddDays(certificateDays));
    private static UptimePing Down(DateTimeOffset at) => new(at, false, null, 20000, null, "The site didn't answer.");

    [Fact]
    public void Client_websites_and_extras_are_watched_once()
    {
        var settings = new UptimeSettings { Extra = ["webignite.co.za", "https://bosveldbuilders.co.za"], Off = ["BOSVELDBUILDERS.co.za"] };
        var domains = Uptime.Domains(Clients, settings);
        Assert.Equal(["karoocoffee.co.za", "webignite.co.za"], domains.Select(d => d.Domain));
        Assert.Equal("Karoo Coffee (shop)", domains[0].Client);
        Assert.Null(domains[1].Client);
    }

    [Fact]
    public void Adding_a_switched_off_client_site_switches_it_back_on()
    {
        var off = Uptime.Remove(new UptimeSettings(), "bosveldbuilders.co.za", Clients);
        Assert.Equal(["bosveldbuilders.co.za"], off.Off);
        var on = Uptime.Add(off, "bosveldbuilders.co.za", Clients);
        Assert.Empty(on.Off);
        Assert.Empty(on.Extra);
        Assert.Equal(["webignite.co.za"], Uptime.Add(on, "webignite.co.za", Clients).Extra);
    }

    [Fact]
    public void Removing_an_extra_site_forgets_it()
    {
        var settings = new UptimeSettings
        {
            Extra = ["webignite.co.za"],
            Sites = new Dictionary<string, WatchedSite> { ["webignite.co.za"] = new() { Domain = "webignite.co.za" } },
        };
        var removed = Uptime.Remove(settings, "webignite.co.za", Clients);
        Assert.Empty(removed.Extra);
        Assert.Empty(removed.Off);
        Assert.Empty(removed.Sites);
    }

    [Fact]
    public void One_failure_is_a_blip_two_is_down_and_coming_back_is_announced()
    {
        var (site, alert) = Uptime.Record(null, "karoocoffee.co.za", Down(Nine), Today);
        Assert.Null(alert);
        Assert.False(Uptime.IsDown(site));
        Assert.Equal("Didn't answer once. Checking again.", Uptime.Status(site, Nine));

        (site, alert) = Uptime.Record(site, "karoocoffee.co.za", Down(Nine.AddMinutes(15)), Today);
        Assert.True(Uptime.IsDown(site));
        Assert.Equal("karoocoffee.co.za is down", alert!.Title);
        Assert.True(alert.Problem);
        Assert.Equal(Nine, site.DownSince);

        (site, alert) = Uptime.Record(site, "karoocoffee.co.za", Down(Nine.AddMinutes(30)), Today);
        Assert.Null(alert);

        (site, alert) = Uptime.Record(site, "karoocoffee.co.za", Up(Nine.AddMinutes(45)), Today);
        Assert.Equal("karoocoffee.co.za is back up", alert!.Title);
        Assert.Equal("It was down for 45 minutes.", alert.Text);
        Assert.False(alert.Problem);
        Assert.Null(site.DownSince);
    }

    [Fact]
    public void A_blip_that_recovers_says_nothing()
    {
        var (site, _) = Uptime.Record(null, "x.co.za", Down(Nine), Today);
        var (_, alert) = Uptime.Record(site, "x.co.za", Up(Nine.AddMinutes(15)), Today);
        Assert.Null(alert);
    }

    [Fact]
    public void Certificate_is_warned_about_once()
    {
        var (site, alert) = Uptime.Record(null, "x.co.za", Up(Nine, certificateDays: 10), Today);
        Assert.Equal("The certificate runs out in 10 days.", alert!.Text);
        (_, alert) = Uptime.Record(site, "x.co.za", Up(Nine.AddMinutes(15), certificateDays: 10), Today);
        Assert.Null(alert);
        Assert.True(Uptime.CertificateProblem(site, Today));
        Assert.Equal("Certificate fine for 60 days", Uptime.Certificate(Uptime.Record(null, "y.co.za", Up(Nine), Today).Site, Today));
    }

    [Fact]
    public void Uptime_counts_the_last_30_days()
    {
        var site = new WatchedSite { Domain = "x.co.za", Days = [new(Today.AddDays(-40), 96, 0), new(Today.AddDays(-1), 96, 95)] };
        (site, _) = Uptime.Record(site, "x.co.za", Up(Nine), Today);
        Assert.Equal(2, site.Days.Count);
        Assert.Equal("99.0% up", Uptime.PercentText(site));
        Assert.Equal("100% up", Uptime.PercentText(new WatchedSite { Domain = "y", Days = [new(Today, 4, 4)] }));
    }

    [Fact]
    public void Slow_sites_say_so()
    {
        var (site, _) = Uptime.Record(null, "x.co.za", Up(Nine, ms: 7300), Today);
        Assert.Equal("Up but slow, 7.3 s", Uptime.Status(site, Nine));
        Assert.Equal("Up, 400 ms", Uptime.Status(Uptime.Record(null, "x.co.za", Up(Nine), Today).Site, Nine));
    }

    [Fact]
    public void Checks_run_every_15_minutes_unless_switched_off()
    {
        var settings = new UptimeSettings();
        Assert.True(Uptime.Due(settings, null, Nine));
        Assert.False(Uptime.Due(settings, Nine.AddMinutes(-10), Nine));
        Assert.True(Uptime.Due(settings, Nine.AddMinutes(-15), Nine));
        Assert.False(Uptime.Due(settings with { On = false }, null, Nine));
    }

    [Fact]
    public void Morning_summary_lists_down_sites_and_certificates()
    {
        var (down, _) = Uptime.Record(null, "karoocoffee.co.za", Down(Nine), Today);
        (down, _) = Uptime.Record(down, "karoocoffee.co.za", Down(Nine.AddMinutes(15)), Today);
        var (expiring, _) = Uptime.Record(null, "bosveldbuilders.co.za", Up(Nine, certificateDays: 3), Today);
        var (fine, _) = Uptime.Record(null, "webignite.co.za", Up(Nine), Today);
        var lines = MorningSummary.Build(new SummaryInput { Today = Today, Sites = [down, expiring, fine] });
        Assert.Equal(
        [
            $"karoocoffee.co.za is down. It stopped answering at {Nine.ToLocalTime():HH:mm}.",
            "bosveldbuilders.co.za: The certificate runs out in 3 days.",
        ], lines.Where(l => l.Target == SummaryTarget.SiteCheck).Select(l => l.Text));
        Assert.All(lines.Where(l => l.Target == SummaryTarget.SiteCheck), l => Assert.True(l.Problem));
    }
}
