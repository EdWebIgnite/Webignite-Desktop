using System.Globalization;
using Webignite.Core.Documents;
using Webignite.Core.Renewals;
using Webignite.Core.Retainers;

namespace Webignite.Core.Finance;

/// <summary>Where the expected income for a month comes from.</summary>
public enum ExpectedSource
{
    /// <summary>Worked out from invoices due, retainers and charged renewals.</summary>
    Auto,

    /// <summary>Typed in by hand for each month.</summary>
    Manual,
}

/// <summary>Where the income that came in is read from.</summary>
public enum CameInSource
{
    /// <summary>Money received, as typed in on the Money page.</summary>
    Money,

    /// <summary>Invoices Xero says were paid, by the day they were paid.</summary>
    Xero,
}

public enum ChartStyle
{
    /// <summary>Expected and came in as two bars side by side.</summary>
    Bars,

    /// <summary>Came in fills the expected bar, like a progress bar.</summary>
    Filled,

    /// <summary>Two lines across the months.</summary>
    Lines,
}

/// <summary>Eddie's projection settings, kept on this PC.</summary>
public sealed record IncomePlan
{
    public ExpectedSource Expected { get; init; } = ExpectedSource.Auto;
    public CameInSource CameIn { get; init; } = CameInSource.Money;
    public ChartStyle Chart { get; init; } = ChartStyle.Bars;

    /// <summary>Typed-in expected amounts by month, keyed "yyyy-MM". Kept when switching to auto.</summary>
    public IReadOnlyDictionary<string, decimal> Manual { get; init; } = new Dictionary<string, decimal>();
}

/// <summary>One month of the projection. Expected is null when it is manual and nothing was typed.</summary>
public sealed record ProjectionMonth(DateOnly Month, decimal? Expected, decimal CameIn, bool IsPast, bool IsCurrent)
{
    public bool IsFuture => !IsPast && !IsCurrent;

    /// <summary>Expected less what came in. Positive means still to come, or short for a past month.</summary>
    public decimal Gap => (Expected ?? 0) - CameIn;
}

/// <summary>What the money behind an auto expected figure is, for showing under the chart.</summary>
public sealed record ExpectedItem(DateOnly Date, string What, decimal Amount);

public static class Projection
{
    public static string Key(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>The 12 months the chart shows: 5 back, this month and 6 ahead.</summary>
    public static DateOnly FirstMonth(DateOnly today) => Ledger.MonthOf(today).AddMonths(-5);

    public const int MonthCount = 12;

    /// <summary>Sets or clears (null) the typed-in amount for one month.</summary>
    public static IncomePlan SetManual(IncomePlan plan, DateOnly month, decimal? amount)
    {
        var manual = new Dictionary<string, decimal>(plan.Manual);
        if (amount is { } a and >= 0)
            manual[Key(month)] = Money.Cents(a);
        else
            manual.Remove(Key(month));
        return plan with { Manual = manual };
    }

    /// <summary>
    /// What should come in during a month: invoices due that month that aren't voided or deleted,
    /// plus retainer and renewal invoices that haven't been drafted yet but will be.
    /// <paramref name="drafted"/> is every automatic draft key made so far, so nothing is counted twice.
    /// </summary>
    public static IReadOnlyList<ExpectedItem> ExpectedIn(DateOnly month, IEnumerable<StoredDocument> documents,
        IEnumerable<Retainer> retainers, IEnumerable<Renewal> renewals, IReadOnlySet<string> drafted)
    {
        var items = new List<ExpectedItem>();
        var first = Ledger.MonthOf(month);

        foreach (var d in documents)
        {
            if (d.Document.Kind != DocumentKind.Invoice || d.SettledAs is "voided" or "deleted")
                continue;
            if ((d.Document.DueOrExpiryDate ?? d.Document.Date) is not { } due || !Ledger.InMonth(due, first))
                continue;
            var total = d.Document.Total ?? Money.Cents(d.Document.NetSubtotal() + (d.Document.TaxTotal ?? 0));
            if (total > 0)
                items.Add(new ExpectedItem(due, Name(d.Document.Customer.Name, d.XeroNumber ?? d.Document.Reference), total));
        }

        foreach (var r in retainers)
        {
            if (r.Paused || Ledger.MonthOf(r.Starts) > first || r.Ends is { } ends && Ledger.MonthOf(ends) < first)
                continue;
            if (drafted.Contains(RetainerSchedule.Key(r, first)) || r.MonthlyAmount <= 0)
                continue;
            items.Add(new ExpectedItem(RetainerSchedule.DateIn(r, first), Name(r.ClientName, r.Title), r.MonthlyAmount));
        }

        foreach (var r in renewals)
        {
            if (r.Price is not > 0 || string.IsNullOrWhiteSpace(r.ClientName) || r.PeriodMonths < 1)
                continue;
            // Only the next renewal can have been invoiced already; later ones repeat every period.
            for (var on = r.RenewsOn; Ledger.MonthOf(on) <= first; on = on.AddMonths(r.PeriodMonths))
            {
                if (!Ledger.InMonth(on, first))
                    continue;
                if (on == r.RenewsOn && drafted.Contains(RenewalRules.InvoiceKey(r)))
                    continue;
                items.Add(new ExpectedItem(on, Name(r.ClientName, $"{RenewalRules.KindLabel(r.Kind)} renewal {r.Name.Trim()}"), r.Price.Value));
            }
        }

        return items.OrderBy(i => i.Date).ToList();
    }

    /// <summary>What came in during a month, from Money received or from Xero payments.</summary>
    public static decimal CameIn(DateOnly month, CameInSource source, IEnumerable<MoneyEntry> entries, IEnumerable<StoredDocument> documents) =>
        source == CameInSource.Money
            ? Ledger.Sum(entries.Where(e => Ledger.InMonth(e.Date, month)), EntryKind.Received)
            : documents
                .Where(d => d.Document.Kind == DocumentKind.Invoice && d.PaidOn is { } paid && Ledger.InMonth(paid, month))
                .Sum(d => d.XeroAmountPaid ?? d.Document.Total ?? 0);

    /// <summary>The 12 months the chart and table show, oldest first.</summary>
    public static IReadOnlyList<ProjectionMonth> Months(IncomePlan plan, IEnumerable<MoneyEntry> entries,
        IEnumerable<StoredDocument> documents, IEnumerable<Retainer> retainers, IEnumerable<Renewal> renewals,
        IReadOnlySet<string> drafted, DateOnly today)
    {
        var docs = documents.ToList();
        var money = entries.ToList();
        var ret = retainers.ToList();
        var ren = renewals.ToList();
        var current = Ledger.MonthOf(today);
        return Enumerable.Range(0, MonthCount)
            .Select(i => FirstMonth(today).AddMonths(i))
            .Select(m => new ProjectionMonth(m,
                plan.Expected == ExpectedSource.Manual
                    ? plan.Manual.TryGetValue(Key(m), out var typed) ? typed : null
                    : ExpectedIn(m, docs, ret, ren, drafted).Sum(e => e.Amount),
                m > current ? 0 : CameIn(m, plan.CameIn, money, docs),
                m < current, m == current))
            .ToList();
    }

    private static string Name(string client, string? what) =>
        string.IsNullOrWhiteSpace(what) ? client.Trim() : $"{client.Trim()}, {what.Trim()}";
}
