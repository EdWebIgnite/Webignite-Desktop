using Webignite.Core.Web;

namespace Webignite.Tests;

public sealed class SiteCheckExtrasTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    private static Dictionary<string, IReadOnlyList<string>> Dkim(params (string Selector, string Record)[] found) =>
        DomainReport.DkimSelectors.ToDictionary(s => s, s => (IReadOnlyList<string>)found.Where(f => f.Selector == s).Select(f => f.Record).ToList());

    [Fact]
    public void Dkim_found_names_the_selectors()
    {
        var finding = DomainReport.Dkim(Dkim(("google", "v=DKIM1; k=rsa; p=MIIB"), ("selector1", "v=DKIM1; p=MIIC")), hasMail: true);
        Assert.Equal("Set up. Found under google, selector1.", finding.Value);
        Assert.False(finding.Problem);
    }

    [Fact]
    public void Dkim_with_an_empty_key_is_a_problem()
    {
        var finding = DomainReport.Dkim(Dkim(("default", "v=DKIM1; p=")), hasMail: true);
        Assert.True(finding.Problem);
        Assert.StartsWith("Found under default, but the key is empty", finding.Value);
    }

    [Fact]
    public void Dkim_not_found_is_not_shown_as_a_problem()
    {
        var finding = DomainReport.Dkim(Dkim(("default", "v=spf1 ~all")), hasMail: true);
        Assert.False(finding.Problem);
        Assert.StartsWith("None found under the usual names.", finding.Value);
    }

    [Fact]
    public void Dkim_sits_between_spf_and_dmarc()
    {
        var findings = DomainReport.MailAuth(["v=spf1 mx ~all"], ["v=DMARC1; p=none"], true, Dkim(("k1", "k=rsa; p=MIG")));
        Assert.Equal(["SPF", "DKIM", "DMARC"], findings.Select(f => f.Label));
    }

    private static SpeedReport Report(int score) => new(score, [new("Main content shows", "5.8 s", true), new("First thing shows", "1.6 s", false)],
        ["Serve images in modern formats (saves about 2.4 s)"]);

    [Fact]
    public void Speed_runs_are_kept_per_domain_one_a_day()
    {
        var history = SpeedHistory.Add(new Dictionary<string, IReadOnlyList<SpeedRun>>(), "karoo.co.za", Report(38), Today.AddDays(-30));
        history = SpeedHistory.Add(history, "karoo.co.za", Report(40), Today);
        history = SpeedHistory.Add(history, "karoo.co.za", Report(47), Today);
        Assert.Equal([38, 47], history["karoo.co.za"].Select(r => r.Score));
        Assert.Equal("5.8 s", history["karoo.co.za"][1].MainContent);
        Assert.Equal(38, SpeedHistory.Previous(history, "karoo.co.za", Today)!.Score);
        Assert.Null(SpeedHistory.Previous(history, "other.co.za", Today));
    }

    [Fact]
    public void Only_the_last_twelve_are_kept()
    {
        IReadOnlyDictionary<string, IReadOnlyList<SpeedRun>> history = new Dictionary<string, IReadOnlyList<SpeedRun>>();
        for (var i = 0; i < 15; i++)
            history = SpeedHistory.Add(history, "x.co.za", Report(50 + i), Today.AddDays(-i * 7));
        Assert.Equal(SpeedHistory.Keep, history["x.co.za"].Count);
        Assert.Equal(Today, history["x.co.za"][^1].Day);
    }

    [Theory]
    [InlineData(47, "Up from 38 on 2 October.")]
    [InlineData(30, "Down from 38 on 2 October.")]
    [InlineData(38, "The same as on 2 October.")]
    public void Compare_with_last_time(int score, string expected) =>
        Assert.Equal(expected, SpeedHistory.Compare(new SpeedRun(new DateOnly(2026, 10, 2), 38, null), score));

    [Fact]
    public void Report_text_reads_plainly()
    {
        var text = SpeedHistory.ReportText("karoo.co.za", Report(47), new SpeedRun(new DateOnly(2026, 10, 2), 38, null), Today);
        Assert.Equal("""
            Website speed for karoo.co.za, 1 November 2026 (Google PageSpeed, on a phone)
            Score: 47 out of 100 (slow). Up from 38 on 2 October.
            Main content shows: 5.8 s
            First thing shows: 1.6 s

            What would help most:
            - Serve images in modern formats (saves about 2.4 s)
            """.Replace("\r", ""), text);
    }
}
