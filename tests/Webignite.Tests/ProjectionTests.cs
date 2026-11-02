using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Core.Renewals;
using Webignite.Core.Retainers;

namespace Webignite.Tests;

public sealed class ProjectionTests
{
    private static readonly DateOnly Today = Samples.Today; // 31 Oct 2026
    private static readonly DateOnly Nov = new(2026, 11, 1);
    private static readonly HashSet<string> None = [];

    private static StoredDocument Invoice(string id, decimal total, DateOnly due) => new()
    {
        Id = id,
        Document = Samples.Completed(Samples.NewInvoice(Samples.Priced("Work", 1, total))) with { DueOrExpiryDate = due },
        Status = DocumentStatus.InXero,
        XeroNumber = "INV-" + id,
    };

    private static Retainer Seo(DateOnly starts) => new()
    {
        Id = "r1",
        ClientName = "Fynbos Physio",
        Title = "Monthly SEO",
        DayOfMonth = 1,
        Starts = starts,
        Lines = [Samples.Priced("SEO", 1, 2800m)],
    };

    private static Renewal Domain(DateOnly on, decimal? price = 250m) => new()
    {
        Id = "d1",
        Name = "karoocoffee.co.za",
        ClientName = "Karoo Coffee",
        RenewsOn = on,
        PeriodMonths = 12,
        Price = price,
    };

    private static MoneyEntry Received(DateOnly on, decimal amount) => new()
    {
        Id = MoneyEntry.NewId(),
        Kind = EntryKind.Received,
        Date = on,
        Amount = amount,
    };

    [Fact]
    public void Auto_counts_invoices_due_retainers_and_charged_renewals()
    {
        var items = Projection.ExpectedIn(Nov,
            [Invoice("1", 1000m, new DateOnly(2026, 11, 14)), Invoice("2", 999m, new DateOnly(2026, 12, 1))],
            [Seo(new DateOnly(2026, 9, 1))],
            [Domain(new DateOnly(2026, 11, 20)), Domain(new DateOnly(2026, 11, 20), price: null) with { Id = "d2" }],
            None);
        Assert.Equal(1000m + 2800m + 250m, items.Sum(i => i.Amount));
        Assert.Equal(3, items.Count);
        Assert.Equal(new DateOnly(2026, 11, 1), items[0].Date);
    }

    [Fact]
    public void A_drafted_retainer_month_is_not_counted_twice()
    {
        var retainer = Seo(new DateOnly(2026, 9, 1));
        var drafted = new HashSet<string> { RetainerSchedule.Key(retainer, Nov) };
        var items = Projection.ExpectedIn(Nov, [], [retainer], [], drafted);
        Assert.Empty(items);
    }

    [Fact]
    public void Paused_ended_and_not_started_retainers_are_left_out()
    {
        Retainer[] retainers =
        [
            Seo(Nov) with { Id = "a", Paused = true },
            Seo(new DateOnly(2026, 1, 1)) with { Id = "b", Ends = new DateOnly(2026, 10, 1) },
            Seo(new DateOnly(2027, 1, 1)) with { Id = "c" },
        ];
        Assert.Empty(Projection.ExpectedIn(Nov, [], retainers, [], None));
    }

    [Fact]
    public void Voided_invoices_are_not_expected()
    {
        var voided = Invoice("1", 1000m, new DateOnly(2026, 11, 14)) with { SettledAs = "voided" };
        Assert.Empty(Projection.ExpectedIn(Nov, [voided], [], [], None));
    }

    [Fact]
    public void Renewals_repeat_each_period_and_only_the_next_can_be_already_invoiced()
    {
        var renewal = Domain(new DateOnly(2026, 11, 20));
        var drafted = new HashSet<string> { RenewalRules.InvoiceKey(renewal) };
        Assert.Empty(Projection.ExpectedIn(Nov, [], [], [renewal], drafted));
        var nextYear = Projection.ExpectedIn(new DateOnly(2027, 11, 1), [], [], [renewal], drafted);
        Assert.Equal(250m, Assert.Single(nextYear).Amount);
    }

    [Fact]
    public void Came_in_reads_money_received_or_xero_payments()
    {
        MoneyEntry[] entries = [Received(new DateOnly(2026, 10, 3), 500m), Received(new DateOnly(2026, 9, 3), 70m)];
        StoredDocument[] docs = [Invoice("1", 1000m, new DateOnly(2026, 10, 1)) with { PaidOn = new DateOnly(2026, 10, 9), XeroAmountPaid = 1000m }];
        var oct = new DateOnly(2026, 10, 1);
        Assert.Equal(500m, Projection.CameIn(oct, CameInSource.Money, entries, docs));
        Assert.Equal(1000m, Projection.CameIn(oct, CameInSource.Xero, entries, docs));
    }

    [Fact]
    public void Months_run_five_back_to_six_ahead_with_nothing_come_in_for_the_future()
    {
        var months = Projection.Months(new IncomePlan(), [Received(Today, 300m)], [], [Seo(new DateOnly(2026, 1, 1))], [], None, Today);
        Assert.Equal(12, months.Count);
        Assert.Equal(new DateOnly(2026, 5, 1), months[0].Month);
        Assert.Equal(new DateOnly(2027, 4, 1), months[^1].Month);
        var current = Assert.Single(months, m => m.IsCurrent);
        Assert.Equal(300m, current.CameIn);
        Assert.Equal(2800m, current.Expected);
        Assert.All(months.Where(m => m.IsFuture), m => Assert.Equal(0m, m.CameIn));
    }

    [Fact]
    public void Manual_uses_typed_amounts_and_leaves_the_rest_empty()
    {
        var plan = Projection.SetManual(new IncomePlan { Expected = ExpectedSource.Manual }, Nov, 12000.456m);
        var months = Projection.Months(plan, [], [], [Seo(new DateOnly(2026, 1, 1))], [], None, Today);
        Assert.Equal(12000.46m, months.Single(m => m.Month == Nov).Expected);
        Assert.Null(months.Single(m => m.Month == Nov.AddMonths(1)).Expected);
    }

    [Fact]
    public void Clearing_a_manual_month_removes_it_and_switching_keeps_typed_amounts()
    {
        var plan = Projection.SetManual(new IncomePlan { Expected = ExpectedSource.Manual }, Nov, 5000m);
        var auto = plan with { Expected = ExpectedSource.Auto };
        Assert.Equal(5000m, auto.Manual["2026-11"]);
        Assert.Empty(Projection.SetManual(plan, Nov, null).Manual);
    }
}
