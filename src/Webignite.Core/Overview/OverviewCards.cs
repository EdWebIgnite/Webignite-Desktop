using System.Globalization;
using Webignite.Core.Documents;

namespace Webignite.Core.Overview;

/// <summary>
/// The quiet line under each money figure on Overview, saying what the figure is made of: how many invoices,
/// how late the latest one is, which quotes expire soon.
/// </summary>
public static class OverviewCards
{
    /// <summary>"From 3 invoices, the oldest dated 13 Oct." or "Nothing owed right now."</summary>
    public static string Owed(IEnumerable<StoredDocument> documents)
    {
        var open = documents.Where(d => Receivables.Outstanding(d) > 0).ToList();
        if (open.Count == 0)
            return "Nothing owed right now.";
        var oldest = open.Select(d => d.Document.Date).Where(d => d is not null).Min();
        var from = open.Count == 1 ? "From 1 invoice" : $"From {open.Count} invoices";
        return oldest is { } date ? $"{from}, the oldest dated {Day(date)}." : from + ".";
    }

    /// <summary>"2 invoices, the oldest 14 days late." or "Nothing past its due date."</summary>
    public static string Overdue(IEnumerable<StoredDocument> documents, DateOnly today)
    {
        var late = documents.Where(d => Receivables.IsOverdue(d, today)).ToList();
        if (late.Count == 0)
            return "Nothing past its due date.";
        var days = late.Max(d => today.DayNumber - d.Document.DueOrExpiryDate!.Value.DayNumber);
        var lateness = days == 1 ? "1 day late" : $"{days} days late";
        return late.Count == 1 ? $"1 invoice, {lateness}." : $"{late.Count} invoices, the oldest {lateness}.";
    }

    /// <summary>"3 quotes, 1 expires this week." or "2 quotes waiting on clients." or "No quotes waiting."</summary>
    public static string QuotesOut(IEnumerable<StoredDocument> documents, DateOnly today)
    {
        var open = documents.Where(d => Receivables.IsOpenQuote(d, today)).ToList();
        if (open.Count == 0)
            return "No quotes waiting.";
        var soon = open.Count(d => d.Document.DueOrExpiryDate is { } expires && expires <= today.AddDays(7));
        var count = open.Count == 1 ? "1 quote" : $"{open.Count} quotes";
        return soon switch
        {
            0 => open.Count == 1 ? "1 quote waiting on a client." : $"{count} waiting on clients.",
            1 => $"{count}, 1 expires this week.",
            _ => $"{count}, {soon} expire this week.",
        };
    }

    private static string Day(DateOnly date) => date.ToString("d MMM", CultureInfo.InvariantCulture);
}
