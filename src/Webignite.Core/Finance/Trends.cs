using System.Globalization;
using Webignite.Core.Documents;

namespace Webignite.Core.Finance;

/// <summary>
/// Puts a money figure next to the one before it, so a number says whether things are better or worse.
/// Comparisons are always like with like: this month so far against last month up to the same day, this tax
/// year so far against last tax year up to the same date.
/// </summary>
public static class Trends
{
    /// <summary>Money received from the first of this month to today.</summary>
    public static decimal ReceivedThisMonth(IEnumerable<MoneyEntry> entries, DateOnly today) =>
        Received(entries, Dates.MonthStart(today), today);

    /// <summary>
    /// "R 2 800.00 more than by 3 Oct." or "R 500.00 less than by 3 Oct." or "The same as by 3 Oct." Last month
    /// counts up to the same day, or its last day when it was shorter. Null when nothing came in either time.
    /// </summary>
    public static string? AgainstLastMonth(IEnumerable<MoneyEntry> entries, DateOnly today)
    {
        var list = entries.ToList();
        var lastStart = Dates.MonthStart(today).AddMonths(-1);
        var sameDay = lastStart.AddDays(Math.Min(today.Day, DateTime.DaysInMonth(lastStart.Year, lastStart.Month)) - 1);
        return Compare(Received(list, Dates.MonthStart(today), today), Received(list, lastStart, sameDay),
            "by " + sameDay.ToString("d MMM", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The same comparison for the tax year: this one so far against last one up to the same date.
    /// "R 12 000.00 more than by this date last tax year." Null when nothing came in either time.
    /// </summary>
    public static string? AgainstLastTaxYear(IEnumerable<MoneyEntry> entries, DateOnly today)
    {
        var list = entries.ToList();
        var start = Dates.TaxYear(today).From;
        var lastStart = start.AddYears(-1);
        // 29 February has no match a year earlier; AddYears lands on the 28th.
        var sameDate = today.AddYears(-1);
        return Compare(Received(list, start, today), Received(list, lastStart, sameDate), "by this date last tax year");
    }

    /// <summary>Money received in each of the last <paramref name="count"/> months, oldest first, this month so far last.</summary>
    public static IReadOnlyList<decimal> ReceivedByMonth(IEnumerable<MoneyEntry> entries, DateOnly today, int count = 6) =>
        Ledger.Months(entries.Where(e => e.Date <= today), today, count).Select(m => m.Received).ToList();

    private static decimal Received(IEnumerable<MoneyEntry> entries, DateOnly from, DateOnly to) =>
        Ledger.Sum(entries.Where(e => e.Date >= from && e.Date <= to), EntryKind.Received);

    private static string? Compare(decimal now, decimal then, string when)
    {
        if (now == 0 && then == 0)
            return null;
        var difference = now - then;
        return difference switch
        {
            > 0 => $"{Money.Rand(difference)} more than {when}.",
            < 0 => $"{Money.Rand(-difference)} less than {when}.",
            _ => $"The same as {when}.",
        };
    }
}
