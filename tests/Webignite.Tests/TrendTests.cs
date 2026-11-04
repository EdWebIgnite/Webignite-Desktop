using Webignite.Core.Finance;
using Webignite.Core.Overview;

namespace Webignite.Tests;

/// <summary>Numbers with context: this month against last, the tax year against last, and what each Overview figure is made of.</summary>
public sealed class TrendTests
{
    private static readonly DateOnly Today = Samples.Today; // Saturday 31 Oct 2026

    private static MoneyEntry In(DateOnly date, decimal amount) =>
        new() { Id = MoneyEntry.NewId(), Kind = EntryKind.Received, Date = date, Amount = amount };

    private static MoneyEntry Out(DateOnly date, decimal amount) =>
        new() { Id = MoneyEntry.NewId(), Kind = EntryKind.Expense, Date = date, Amount = amount, Party = "Supplier" };

    [Fact]
    public void This_month_is_compared_with_last_month_up_to_the_same_day()
    {
        var today = new DateOnly(2026, 11, 3);
        MoneyEntry[] entries = [In(new(2026, 11, 2), 4000m), In(new(2026, 10, 2), 1200m), In(new(2026, 10, 20), 9000m), Out(new(2026, 11, 1), 500m)];
        Assert.Equal(4000m, Trends.ReceivedThisMonth(entries, today));
        Assert.Equal($"{Money.Rand(2800m)} more than by 3 Oct.", Trends.AgainstLastMonth(entries, today));
    }

    [Fact]
    public void Less_and_the_same_read_plainly_and_nothing_either_time_says_nothing()
    {
        var today = new DateOnly(2026, 11, 3);
        Assert.Equal($"{Money.Rand(500m)} less than by 3 Oct.", Trends.AgainstLastMonth([In(new(2026, 11, 1), 700m), In(new(2026, 10, 1), 1200m)], today));
        Assert.Equal("The same as by 3 Oct.", Trends.AgainstLastMonth([In(new(2026, 11, 1), 700m), In(new(2026, 10, 1), 700m)], today));
        Assert.Null(Trends.AgainstLastMonth([Out(new(2026, 11, 1), 700m)], today));
    }

    [Fact]
    public void A_shorter_last_month_counts_to_its_last_day()
    {
        // 31 March against February, which ends on the 28th in 2027.
        var text = Trends.AgainstLastMonth([In(new(2027, 2, 28), 1000m)], new DateOnly(2027, 3, 31));
        Assert.Equal($"{Money.Rand(1000m)} less than by 28 Feb.", text);
    }

    [Fact]
    public void The_tax_year_is_compared_with_last_tax_year_up_to_the_same_date()
    {
        MoneyEntry[] entries =
        [
            In(new(2026, 3, 5), 10000m), In(new(2026, 10, 30), 5000m),  // this tax year
            In(new(2025, 4, 1), 8000m), In(new(2025, 11, 15), 9000m),   // last tax year; November is after the same date
            In(new(2025, 2, 20), 99000m),                               // the tax year before
        ];
        Assert.Equal($"{Money.Rand(7000m)} more than by this date last tax year.", Trends.AgainstLastTaxYear(entries, Today));
    }

    [Fact]
    public void Monthly_received_runs_oldest_first_and_ignores_future_entries()
    {
        var months = Trends.ReceivedByMonth([In(new(2026, 10, 3), 2800m), In(new(2026, 9, 1), 1000m), In(new(2026, 11, 2), 500m)], Today, 3);
        Assert.Equal([0m, 1000m, 2800m], months);
    }

    private static StoredDocument Invoice(decimal amount, DateOnly date, DateOnly due, Settlement? settledAs = null) => new()
    {
        Id = StoredDocument.NewId(),
        Status = DocumentStatus.InXero,
        XeroStatus = "AUTHORISED",
        SettledAs = settledAs,
        Settled = settledAs is null ? null : DateTimeOffset.UtcNow,
        Document = Samples.Completed(Samples.NewInvoice(Samples.Priced("Website", 1, amount))) with { Date = date, DueOrExpiryDate = due },
    };

    private static StoredDocument Quote(DateOnly expires) => Invoice(1000m, Today.AddDays(-5), expires) with
    {
        Document = Invoice(1000m, Today.AddDays(-5), expires).Document with { Kind = DocumentKind.Quote },
        XeroStatus = "SENT",
    };

    [Fact]
    public void Owed_says_how_many_invoices_and_the_oldest()
    {
        StoredDocument[] docs = [Invoice(1000m, new(2026, 10, 13), new(2026, 10, 20)), Invoice(500m, new(2026, 10, 28), new(2026, 11, 4)),
            Invoice(700m, new(2026, 9, 1), new(2026, 9, 8), settledAs: Settlement.Paid)];
        Assert.Equal("From 2 invoices, the oldest dated 13 Oct.", OverviewCards.Owed(docs));
        Assert.Equal("Nothing owed right now.", OverviewCards.Owed([]));
    }

    [Fact]
    public void Overdue_says_how_late_the_oldest_is()
    {
        StoredDocument[] docs = [Invoice(1000m, new(2026, 10, 1), new(2026, 10, 17)), Invoice(500m, new(2026, 10, 20), new(2026, 10, 30)),
            Invoice(500m, new(2026, 10, 28), new(2026, 11, 4))];
        Assert.Equal("2 invoices, the oldest 14 days late.", OverviewCards.Overdue(docs, Today));
        Assert.Equal("1 invoice, 1 day late.", OverviewCards.Overdue([docs[1]], Today));
        Assert.Equal("Nothing past its due date.", OverviewCards.Overdue([docs[2]], Today));
    }

    [Fact]
    public void Quotes_out_points_out_the_ones_expiring_this_week()
    {
        Assert.Equal("2 quotes, 1 expires this week.", OverviewCards.QuotesOut([Quote(Today.AddDays(3)), Quote(Today.AddDays(20))], Today));
        Assert.Equal("2 quotes, 2 expire this week.", OverviewCards.QuotesOut([Quote(Today.AddDays(3)), Quote(Today.AddDays(7))], Today));
        Assert.Equal("1 quote waiting on a client.", OverviewCards.QuotesOut([Quote(Today.AddDays(20))], Today));
        Assert.Equal("No quotes waiting.", OverviewCards.QuotesOut([Quote(Today.AddDays(-1))], Today));
    }
}
