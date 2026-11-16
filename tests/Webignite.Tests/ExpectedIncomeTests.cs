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
