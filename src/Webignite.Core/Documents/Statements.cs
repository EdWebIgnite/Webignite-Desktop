using System.Globalization;

namespace Webignite.Core.Documents;

/// <summary>One unpaid invoice on a statement, as Xero has it.</summary>
public sealed record StatementLine(string Number, DateOnly? Date, DateOnly? Due, decimal Total, decimal AmountDue, string? Reference);

/// <summary>Which clients get a statement this month, and what goes in it.</summary>
public static class Statements
{
    /// <summary>Statements are offered in the first week of the month.</summary>
    public const int OfferDays = 7;

    public static bool IsStatementWeek(DateOnly today) => today.Day <= OfferDays;

    /// <summary>
    /// Customers with invoices in Xero that aren't known to be paid, who haven't had a statement this month.
    /// The amounts here are from this PC; the statement itself is built from Xero when it's drafted.
    /// </summary>
    public static IReadOnlyList<(string Customer, decimal Open)> Due(IEnumerable<StoredDocument> documents, IReadOnlyDictionary<string, DateOnly> sent, DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        return documents
            .Where(d => d.Document.Kind == DocumentKind.Invoice && d.Status == DocumentStatus.InXero && d.XeroId is not null && d.Settled is null)
            .GroupBy(d => d.Document.Customer.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => !(sent.TryGetValue(Key(g.Key), out var last) && last >= monthStart))
            .Select(g => (g.Key, g.Sum(d => d.Document.Total ?? 0)))
            .OrderByDescending(x => x.Item2)
            .ToList();
    }

    /// <summary>How a customer is remembered in the "statements sent" list.</summary>
    public static string Key(string customer) => Xero.XeroRules.Normalise(customer);

    /// <summary>The invoices as text for the mail, oldest first, with how late each one is.</summary>
    public static string Lines(IEnumerable<StatementLine> lines, DateOnly today)
    {
        var text = new List<string>();
        foreach (var line in lines.Where(l => l.AmountDue > 0).OrderBy(l => l.Date))
        {
            var parts = new List<string> { line.Number };
            if (line.Date is { } date)
                parts.Add(date.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
            if (line.Reference is { Length: > 0 } reference)
                parts.Add(reference);
            var late = line.Due is { } due && due < today ? $", {Days(today.DayNumber - due.DayNumber)} overdue"
                : line.Due is { } d ? $", due {d.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}" : "";
            text.Add($"- {string.Join(", ", parts)}: {Money.Rand(line.AmountDue)}{late}");
        }
        return string.Join("\n", text);
    }

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

    /// <summary>The contact to build a statement for: one already linked to Xero on a client or invoice, if any.</summary>
    public static string? ContactIdFor(string customer, IEnumerable<StoredDocument> documents, IEnumerable<Clients.Client> clients)
    {
        var key = Key(customer);
        return clients.FirstOrDefault(c => Key(c.Name) == key && c.XeroContactId is { Length: > 0 })?.XeroContactId
            ?? documents.Where(d => Key(d.Document.Customer.Name) == key && d.XeroContactId is { Length: > 0 })
                .OrderByDescending(d => d.Updated).FirstOrDefault()?.XeroContactId;
    }

    /// <summary>Remembers that a customer had their statement, so it isn't offered again this month.</summary>
    public static IReadOnlyDictionary<string, DateOnly> MarkSent(IReadOnlyDictionary<string, DateOnly> sent, string customer, DateOnly today)
    {
        var copy = new Dictionary<string, DateOnly>(sent) { [Key(customer)] = today };
        // Nothing older than a year matters any more.
        foreach (var old in copy.Where(p => p.Value < today.AddYears(-1)).Select(p => p.Key).ToList())
            copy.Remove(old);
        return copy;
    }

    public static decimal TotalDue(IEnumerable<StatementLine> lines) => lines.Sum(l => Math.Max(0, l.AmountDue));
}
