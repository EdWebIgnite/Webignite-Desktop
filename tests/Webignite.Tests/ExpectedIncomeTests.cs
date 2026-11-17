using Webignite.Core.Finance;

namespace Webignite.Tests;

public class ExpectedIncomeTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static ExpectedIncome Line(string id, decimal amount, int percent, int month, int year = 2027) =>
        new() { Id = id, Who = "Client " + id, Amount = amount, Percent = percent, Month = new DateOnly(year, month, 1) };

    [Fact]
    public void A_month_adds_up_what_is_hoped_for_and_what_it_is_likely_worth()
    {
        var months = ExpectedRules.Months([Line("a", 10000m, 50, 1), Line("b", 4000m, 100, 1), Line("c", 3000m, 25, 12, 2026)]);
        Assert.Equal([new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 1)], months.Select(m => m.Month));
        Assert.Equal(14000m, months[1].Possible);
        Assert.Equal(9000m, months[1].Likely);
        Assert.Equal(750m, months[0].Likely);
        Assert.Equal(["b", "a"], months[1].Items.Select(i => i.Id)); // the surest first
    }

    [Fact]
    public void A_line_ticked_off_leaves_the_months_and_can_be_put_back()
    {
        IReadOnlyList<ExpectedIncome> items = [Line("a", 10000m, 50, 1), Line("b", 4000m, 100, 1)];
        items = ExpectedRules.Settle(items, "b", ExpectedOutcome.CameIn, Today);
        Assert.Equal(["a"], ExpectedRules.Months(items).Single().Items.Select(i => i.Id));
        Assert.Equal(Today, ExpectedRules.Settled(items).Single().Settled);
        items = ExpectedRules.Settle(items, "b", ExpectedOutcome.Open, Today);
        Assert.Empty(ExpectedRules.Settled(items));
        Assert.Null(items.Single(i => i.Id == "b").Settled);
    }

    [Theory]
    [InlineData(100, "Confirmed")]
    [InlineData(75, "Likely")]
    [InlineData(60, "Likely")]
    [InlineData(50, "Maybe")]
    [InlineData(25, "Long shot")]
    [InlineData(0, "Long shot")]
    public void A_percentage_has_a_word(int percent, string label) => Assert.Equal(label, ExpectedRules.Label(percent));

    [Fact]
    public void Each_preset_reads_back_as_its_own_label()
    {
        foreach (var (label, percent) in ExpectedRules.Presets)
            Assert.Equal(label, ExpectedRules.Label(percent));
    }

    [Fact]
    public void How_sure_is_a_whole_number_up_to_a_hundred()
    {
        Assert.Equal(60, ExpectedRules.ParsePercent(" 60% "));
        Assert.Null(ExpectedRules.ParsePercent("101"));
        Assert.Null(ExpectedRules.ParsePercent("-5"));
        Assert.Null(ExpectedRules.ParsePercent("sure"));
    }

    [Fact]
    public void A_line_needs_an_amount_and_who_or_what()
    {
        Assert.NotNull(ExpectedRules.Problem(new ExpectedIncome { Id = "x", Who = "G3" }));
        Assert.NotNull(ExpectedRules.Problem(new ExpectedIncome { Id = "x", Amount = 500m }));
        Assert.Null(ExpectedRules.Problem(new ExpectedIncome { Id = "x", What = "Hosting", Amount = 500m }));
    }

    [Fact]
    public void Changing_a_line_keeps_one_copy_and_the_list_is_not_a_shared_setting()
    {
        var items = ExpectedRules.Put([Line("a", 100m, 50, 1)], Line("a", 250m, 75, 2));
        Assert.Equal(250m, Assert.Single(items).Amount);
        Assert.False(Webignite.Core.Sync.SharedSettings.IsShared("expected-income"));
        Assert.Equal(new DateOnly(2026, 11, 1), ExpectedRules.MonthChoices(Today)[0]);
        Assert.Equal(19, ExpectedRules.MonthChoices(Today).Count);
    }
}

public class NeedsYouAndNotesTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    [Fact]
    public void A_dismissed_line_stays_away_while_its_words_change_and_a_problem_comes_back_after_a_week()
    {
        var followUp = new Webignite.Core.Overview.SummaryLine("QU-1020 for Eedria was emailed 7 days ago with no answer yet.", Webignite.Core.Overview.SummaryTarget.Renewals) { Key = "chase:q1:FollowUp" };
        var later = followUp with { Text = "QU-1020 for Eedria was emailed 30 days ago with no answer yet." };
        var problem = new Webignite.Core.Overview.SummaryLine("karoocoffee.co.za is down.", Webignite.Core.Overview.SummaryTarget.SiteCheck, Problem: true) { Key = "site:karoocoffee.co.za:down" };

        var dismissed = Webignite.Core.Overview.MorningSummary.Dismiss(new Dictionary<string, DateOnly>(), followUp, Today);
        dismissed = Webignite.Core.Overview.MorningSummary.Dismiss(dismissed, problem, Today);
        Assert.True(Webignite.Core.Overview.MorningSummary.IsDismissed(dismissed, later, Today.AddDays(200)));
        Assert.True(Webignite.Core.Overview.MorningSummary.IsDismissed(dismissed, problem, Today.AddDays(6)));
        Assert.False(Webignite.Core.Overview.MorningSummary.IsDismissed(dismissed, problem, Today.AddDays(7)));
        // Undo, and a line with no key of its own is known by its words without the numbers.
        Assert.False(Webignite.Core.Overview.MorningSummary.IsDismissed(Webignite.Core.Overview.MorningSummary.PutBack(dismissed, followUp), later, Today));
        var plain = new Webignite.Core.Overview.SummaryLine("3 more overdue invoices.", Webignite.Core.Overview.SummaryTarget.Invoices);
        Assert.Equal(plain.Key, (plain with { Text = "5 more overdue invoices." }).Key);
        // Dismissals over a year old are forgotten.
        Assert.DoesNotContain("chase:q1:FollowUp", Webignite.Core.Overview.MorningSummary.Dismiss(dismissed, problem, Today.AddDays(401)).Keys);
    }

    [Fact]
    public void Notes_are_kept_newest_first_until_dismissed_but_not_the_offers_to_undo()
    {
        var at = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
        IReadOnlyList<Webignite.Core.Overview.KeptNote> kept = [];
        kept = Webignite.Core.Overview.NoteHistory.Add(kept, "New mail", "Thandi: Product photos", at);
        kept = Webignite.Core.Overview.NoteHistory.Add(kept, "New mail", "Thandi: Product photos", at.AddSeconds(20));
        kept = Webignite.Core.Overview.NoteHistory.Add(kept, "Deleted the quote", Webignite.Core.Overview.NoteHistory.UndoText, at.AddMinutes(1));
        kept = Webignite.Core.Overview.NoteHistory.Add(kept, "Site down", "karoocoffee.co.za stopped answering.", at.AddMinutes(2));
        Assert.Equal(["Site down", "New mail"], kept.Select(n => n.Title));
        Assert.Equal(2, Webignite.Core.Overview.NoteHistory.Unseen(kept));
        kept = Webignite.Core.Overview.NoteHistory.Seen(kept);
        Assert.Equal(0, Webignite.Core.Overview.NoteHistory.Unseen(kept));
        Assert.Equal("New mail", Assert.Single(Webignite.Core.Overview.NoteHistory.Dismiss(kept, kept[0].Id)).Title);
        for (var i = 0; i < 150; i++)
            kept = Webignite.Core.Overview.NoteHistory.Add(kept, "Note " + i, "text", at.AddHours(i));
        Assert.Equal(Webignite.Core.Overview.NoteHistory.Most, kept.Count);
    }
}

public class PastedTextTests
{
    [Theory]
    [InlineData("Hi&nbsp;Ronald", "Hi Ronald")]
    [InlineData("We&apos;ll&nbsp;send&nbsp;the&nbsp;quotes", "We'll send the quotes")]
    [InlineData("Hi&amp;nbsp;Ronald, it&amp;apos;s fine", "Hi Ronald, it's fine")]
    [InlineData("Tom &amp; Jerry", "Tom & Jerry")]
    [InlineData("R&D and Q&A stay as typed", "R&D and Q&A stay as typed")]
    [InlineData("5 < 6 & 7 > 2", "5 < 6 & 7 > 2")]
    [InlineData("Non breaking", "Non breaking")]
    public void Text_copied_with_web_codes_left_in_is_pasted_as_it_reads(string copied, string pasted) =>
        Assert.Equal(pasted, Webignite.Core.Writing.RichHtml.Uncoded(copied));

    [Fact]
    public void A_page_with_its_codes_coded_twice_is_read_as_it_reads()
    {
        var read = Webignite.Core.Writing.RichHtml.Read("<p>Hi&amp;nbsp;Ronald</p><ul><li>Design&nbsp;of&nbsp;a&nbsp;site</li></ul>");
        Assert.Equal(["Hi Ronald", "Design of a site"], read.Blocks.OfType<Webignite.Core.Writing.TextBlock>().Select(b => b.Text));
    }
}
