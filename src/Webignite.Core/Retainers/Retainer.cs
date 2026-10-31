using System.Globalization;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Finance;

namespace Webignite.Core.Retainers;

/// <summary>A client paying the same every month, e.g. hosting or SEO. The app drafts the invoice each month.</summary>
public sealed record Retainer
{
    public required string Id { get; init; }
    public required string ClientName { get; init; }

    /// <summary>What it is, e.g. "Monthly SEO". Used as the invoice reference with the month added.</summary>
    public required string Title { get; init; }

    /// <summary>Invoice lines. "[Month]" in a description becomes the month, e.g. "Hosting, [Month]".</summary>
    public required IReadOnlyList<Line> Lines { get; init; }

    /// <summary>Day of the month the invoice is dated. 31 means the last day in shorter months.</summary>
    public int DayOfMonth { get; init; } = 1;

    public required DateOnly Starts { get; init; }
    public DateOnly? Ends { get; init; }
    public bool Paused { get; init; }
    public DateTimeOffset Created { get; init; }

    public decimal MonthlyAmount => Money.Cents(Lines.Sum(l => l.Amount()));

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

public static class RetainerSchedule
{
    public const string MonthPlaceholder = "[Month]";

    public static string Key(Retainer retainer, DateOnly month) => $"retainer:{retainer.Id}:{month:yyyy-MM}";

    public static DateOnly DateIn(Retainer retainer, DateOnly month) =>
        new(month.Year, month.Month, Math.Min(Math.Clamp(retainer.DayOfMonth, 1, 31), DateTime.DaysInMonth(month.Year, month.Month)));

    private static string MonthName(DateOnly month) => month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>The draft invoice for one month, with "[Month]" filled in and totals worked out.</summary>
    public static Document InvoiceFor(Retainer retainer, DateOnly month, DocumentSettings settings) =>
        DocumentDefaults.Complete(new Document
        {
            Kind = DocumentKind.Invoice,
            Origin = DocumentOrigin.New,
            Customer = new Customer { Name = retainer.ClientName.Trim() },
            Reference = $"{retainer.Title.Trim()} {MonthName(month)}",
            Date = DateIn(retainer, month),
            Lines = retainer.Lines
                .Select(l => l with { Description = l.Description.Replace(MonthPlaceholder, MonthName(month), StringComparison.OrdinalIgnoreCase) })
                .ToList(),
        }, settings, DateIn(retainer, month));

    /// <summary>
    /// The invoices retainers call for up to today that haven't been drafted yet, as (key, document) pairs.
    /// <paramref name="drafted"/> holds every key ever drafted, so deleting a draft doesn't bring it back.
    /// </summary>
    public static IReadOnlyList<(string Key, Document Invoice)> Due(IEnumerable<Retainer> retainers, IReadOnlySet<string> drafted,
        DocumentSettings settings, DateOnly today)
    {
        var due = new List<(string, Document)>();
        foreach (var retainer in retainers.Where(r => !r.Paused))
        {
            var last = Ledger.MonthOf(retainer.Ends is { } ends && ends < today ? ends : today);
            for (var month = Ledger.MonthOf(retainer.Starts); month <= last; month = month.AddMonths(1))
            {
                var key = Key(retainer, month);
                if (DateIn(retainer, month) <= today && !drafted.Contains(key))
                    due.Add((key, InvoiceFor(retainer, month, settings)));
            }
        }
        return due;
    }

    /// <summary>The next date an invoice will be drafted, or null if it is paused or has ended.</summary>
    public static DateOnly? NextDate(Retainer retainer, IReadOnlySet<string> drafted, DateOnly today)
    {
        if (retainer.Paused)
            return null;
        var month = Ledger.MonthOf(retainer.Starts > today ? retainer.Starts : today);
        for (var i = 0; i < 24; i++, month = month.AddMonths(1))
        {
            if (retainer.Ends is { } ends && month > Ledger.MonthOf(ends))
                return null;
            if (!drafted.Contains(Key(retainer, month)))
                return DateIn(retainer, month);
        }
        return null;
    }

    /// <summary>Resuming starts again from this month, so the months it was paused aren't drafted afterwards.</summary>
    public static Retainer SetPaused(Retainer retainer, bool paused, DateOnly today) => paused
        ? retainer with { Paused = true }
        : retainer with { Paused = false, Starts = Ledger.MonthOf(today) > retainer.Starts ? Ledger.MonthOf(today) : retainer.Starts };

    public static decimal MonthlyTotal(IEnumerable<Retainer> retainers, DateOnly today) =>
        retainers.Where(r => !r.Paused && (r.Ends is null || Ledger.MonthOf(r.Ends.Value) >= Ledger.MonthOf(today))).Sum(r => r.MonthlyAmount);

    public static IReadOnlyList<string> Check(Retainer retainer)
    {
        var problems = new List<string>();
        if (ClientDirectory.Key(retainer.ClientName).Length == 0)
            problems.Add("Choose the client.");
        if (string.IsNullOrWhiteSpace(retainer.Title))
            problems.Add("Give it a name, like Monthly SEO.");
        if (retainer.DayOfMonth is < 1 or > 31)
            problems.Add("The day of the month is 1 to 31.");
        if (retainer.Lines.Count == 0)
            problems.Add("Add at least one line.");
        else if (retainer.MonthlyAmount <= 0)
            problems.Add("The monthly amount must be more than R 0.");
        if (retainer.Lines.Any(l => string.IsNullOrWhiteSpace(l.Description)))
            problems.Add("Every line needs a description.");
        if (retainer.Lines.Any(l => l.Quantity is null || l.UnitPrice is null))
            problems.Add("Every line needs a quantity and a price.");
        if (retainer.Ends is { } ends && Ledger.MonthOf(ends) < Ledger.MonthOf(retainer.Starts))
            problems.Add("It can't end before it starts.");
        return problems;
    }
}
