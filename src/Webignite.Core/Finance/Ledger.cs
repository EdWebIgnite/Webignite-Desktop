using Webignite.Core.Clients;

namespace Webignite.Core.Finance;

/// <summary>Money in and out for one calendar month.</summary>
public sealed record MonthTotals(int Year, int Month, decimal Received, decimal Expenses)
{
    public decimal LeftOver => Received - Expenses;
    public DateOnly First => new(Year, Month, 1);
}

public sealed record NamedTotal(string Name, decimal Amount);

/// <summary>One client's part of the money that came in: amount, percentage of the whole, and size next to the biggest.</summary>
public sealed record ClientShare(string Name, decimal Amount, string Percent, double OfBiggest);

public static class Ledger
{
    public static bool InMonth(DateOnly date, DateOnly month) => date.Year == month.Year && date.Month == month.Month;

    /// <summary>Totals for <paramref name="count"/> months ending with the month of <paramref name="last"/>, oldest first.</summary>
    public static IReadOnlyList<MonthTotals> Months(IEnumerable<MoneyEntry> entries, DateOnly last, int count)
    {
        var list = entries.ToList();
        var end = Dates.MonthStart(last);
        return Enumerable.Range(0, count)
            .Select(i => end.AddMonths(i - count + 1))
            .Select(m => new MonthTotals(m.Year, m.Month,
                Sum(list.Where(e => InMonth(e.Date, m)), EntryKind.Received),
                Sum(list.Where(e => InMonth(e.Date, m)), EntryKind.Expense)))
            .ToList();
    }

    /// <summary>
    /// "On average R 12 500.00 a month came in over the last 9 full months.", from monthly totals oldest first
    /// ending with this month. This month isn't full yet, and empty months don't count. Empty when there's nothing.
    /// </summary>
    public static string AverageReceived(IReadOnlyList<MonthTotals> months)
    {
        var full = months.Take(months.Count - 1).Where(m => m.Received > 0 || m.Expenses > 0).ToList();
        return full.Count == 0 ? ""
            : $"On average {Webignite.Core.Documents.Money.Rand(full.Average(m => m.Received))} a month came in over the last {Wording.Count(full.Count, "full month")}.";
    }

    public static decimal Sum(IEnumerable<MoneyEntry> entries, EntryKind kind) =>
        entries.Where(e => e.Kind == kind).Sum(e => e.Amount);

    /// <summary>Who paid the most between two dates, biggest first. Names are grouped the same way clients are matched.</summary>
    public static IReadOnlyList<NamedTotal> TopPayers(IEnumerable<MoneyEntry> entries, DateOnly from, DateOnly to, int take = 10) =>
        entries
            .Where(e => e.Kind == EntryKind.Received && e.Date >= from && e.Date <= to)
            .GroupBy(e => ClientDirectory.Key(e.Party))
            .Select(g => new NamedTotal(g.Key.Length == 0 ? "Not said" : g.First().Party!.Trim(), g.Sum(e => e.Amount)))
            .OrderByDescending(t => t.Amount)
            .Take(take)
            .ToList();

    /// <summary>
    /// What each client brought in between two dates, biggest first, every client: the amount, its part of the
    /// whole as a percentage ("38%"), and how it compares to the biggest (0 to 1) for the bar behind it.
    /// </summary>
    public static IReadOnlyList<ClientShare> ByClient(IEnumerable<MoneyEntry> entries, DateOnly from, DateOnly to)
    {
        var totals = TopPayers(entries, from, to, int.MaxValue);
        var whole = totals.Sum(t => t.Amount);
        var biggest = totals.Count == 0 ? 0 : totals[0].Amount;
        return totals.Select(t => new ClientShare(t.Name, t.Amount,
            whole == 0 ? "" : Math.Round(t.Amount / whole * 100, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%",
            biggest == 0 ? 0 : (double)(t.Amount / biggest))).ToList();
    }

    /// <summary>Expenses between two dates by category, biggest first.</summary>
    public static IReadOnlyList<NamedTotal> ByCategory(IEnumerable<MoneyEntry> entries, DateOnly from, DateOnly to) =>
        entries
            .Where(e => e.Kind == EntryKind.Expense && e.Date >= from && e.Date <= to)
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "Other" : e.Category.Trim())
            .Select(g => new NamedTotal(g.Key, g.Sum(e => e.Amount)))
            .OrderByDescending(t => t.Amount)
            .ToList();

    /// <summary>What is wrong with an entry before it can be saved, in plain English. Empty means fine.</summary>
    public static IReadOnlyList<string> Check(MoneyEntry entry, DateOnly today)
    {
        var problems = new List<string>();
        if (entry.Amount <= 0)
            problems.Add("Type an amount more than R 0.");
        else if (entry.Amount != Documents.Money.Cents(entry.Amount))
            problems.Add("Amounts go to cents, two decimals at most.");
        if (entry.Date > today.AddYears(1))
            problems.Add("That date is more than a year away.");
        if (entry.Kind == EntryKind.Expense && string.IsNullOrWhiteSpace(entry.Party) && string.IsNullOrWhiteSpace(entry.Note))
            problems.Add("Say who it was paid to.");
        return problems;
    }
}
