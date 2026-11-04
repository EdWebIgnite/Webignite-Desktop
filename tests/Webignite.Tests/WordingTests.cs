using Webignite.Core;

namespace Webignite.Tests;

/// <summary>Counts, days and dates, written and worked out one way for the whole app.</summary>
public sealed class WordingTests
{
    private static readonly DateOnly Wednesday = new(2026, 11, 4);

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "tomorrow")]
    [InlineData(-1, "yesterday")]
    [InlineData(-5, "5 days ago")]
    [InlineData(2, "Friday")]
    [InlineData(6, "Tuesday")]
    [InlineData(10, "14 Nov")]
    [InlineData(60, "3 Jan 2027")]
    public void A_day_near_today_in_words(int offset, string expected) =>
        Assert.Equal(expected, Wording.Day(Wednesday.AddDays(offset), Wednesday));

    [Fact]
    public void Counts_and_ago()
    {
        Assert.Equal("1 day", Wording.Days(1));
        Assert.Equal("12 days", Wording.Days(12));
        Assert.Equal("3 invoices", Wording.Count(3, "invoice"));
        Assert.Equal("2 entries", Wording.Count(2, "entry", "entries"));
        Assert.Equal("yesterday", Wording.Ago(1));
        Assert.Equal("Yesterday", Wording.Capital("yesterday"));
    }

    [Fact]
    public void Weeks_months_and_tax_years()
    {
        Assert.Equal(new DateOnly(2026, 11, 2), Dates.WeekStart(Wednesday));
        Assert.Equal(new DateOnly(2026, 11, 2), Dates.WeekStart(new DateOnly(2026, 11, 8))); // Sunday ends the week
        Assert.Equal(new DateOnly(2026, 11, 1), Dates.MonthStart(Wednesday));
        Assert.Equal((new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28)), Dates.TaxYear(Wednesday));
        Assert.Equal((new DateOnly(2027, 3, 1), new DateOnly(2028, 2, 29)), Dates.TaxYear(new DateOnly(2027, 3, 1)));
    }

    [Fact]
    public void A_renewal_says_how_far_off_it_is()
    {
        Webignite.Core.Renewals.Renewal Renews(int days) => new() { Id = "r1", Name = "karoocoffee.co.za", RenewsOn = Wednesday.AddDays(days) };
        Assert.Equal("14 Nov 2026, in 10 days", Webignite.Core.Renewals.RenewalRules.When(Renews(10), Wednesday));
        Assert.Equal("5 Nov 2026, tomorrow", Webignite.Core.Renewals.RenewalRules.When(Renews(1), Wednesday));
        Assert.Equal("1 Nov 2026, 3 days ago", Webignite.Core.Renewals.RenewalRules.When(Renews(-3), Wednesday));
        Assert.Equal("3 Feb 2027", Webignite.Core.Renewals.RenewalRules.When(Renews(91), Wednesday));
    }
}
