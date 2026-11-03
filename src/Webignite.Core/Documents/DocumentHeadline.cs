using System.Globalization;

namespace Webignite.Core.Documents;

/// <summary>
/// The words around the quote and invoice lists: the one line under the page title that says where things stand,
/// and the quiet second line under each row.
/// </summary>
public static class DocumentHeadline
{
    /// <summary>
    /// "4 invoices, R 5 600.00 owed, 1 overdue, 1 not in Xero yet" or "3 quotes, R 14 500.00 waiting on clients,
    /// 1 accepted". Parts with nothing to say are left out.
    /// </summary>
    public static string For(DocumentKind kind, IReadOnlyCollection<StoredDocument> documents, DateOnly today)
    {
        var mine = documents.Where(d => d.Document.Kind == kind).ToList();
        var noun = kind == DocumentKind.Quote ? "quote" : "invoice";
        var parts = new List<string> { Count(mine.Count, noun) };

        if (kind == DocumentKind.Invoice)
        {
            var owed = Receivables.Owed(mine);
            if (owed > 0)
                parts.Add(Money.Rand(owed) + " owed");
            var overdue = mine.Count(d => Receivables.IsOverdue(d, today));
            if (overdue > 0)
                parts.Add($"{overdue} overdue");
        }
        else
        {
            var waiting = Receivables.QuotesOut(mine, today);
            if (waiting > 0)
                parts.Add(Money.Rand(waiting) + " waiting on clients");
            var accepted = mine.Count(d => d.SettledAs is "accepted" or "invoiced");
            if (accepted > 0)
                parts.Add($"{accepted} accepted");
        }

        var onThisPc = mine.Count(d => d.Status != DocumentStatus.InXero);
        if (onThisPc > 0)
            parts.Add($"{onThisPc} not in Xero yet");
        return string.Join(", ", parts);
    }

    /// <summary>
    /// The second line under a row: the reference, then when it was paid or when it is due. "Website phase 2,
    /// due 17 Nov 2026" or "Paid 2 Nov 2026" or "Due 1 Oct 2026, 30 days ago". Empty when there is nothing to add.
    /// </summary>
    public static string Detail(StoredDocument stored, DateOnly today)
    {
        var doc = stored.Document;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(doc.Reference))
            parts.Add(doc.Reference.Trim());

        if (stored.SettledAs == "paid")
            parts.Add(stored.PaidOn is { } paid ? "Paid " + Day(paid) : "Paid");
        else if (stored.SettledAs is null && doc.DueOrExpiryDate is { } due)
        {
            var word = doc.Kind == DocumentKind.Quote ? "Expires" : "Due";
            if (Receivables.IsOverdue(stored, today))
            {
                var late = today.DayNumber - due.DayNumber;
                parts.Add($"{word} {Day(due)}, {(late == 1 ? "yesterday" : $"{late} days ago")}");
            }
            else if (doc.Kind == DocumentKind.Quote && due < today)
                parts.Add("Expired " + Day(due));
            else
                parts.Add($"{word} {Day(due)}");
        }

        if (parts.Count > 1)
            parts[1] = char.ToLowerInvariant(parts[1][0]) + parts[1][1..];
        return string.Join(", ", parts);
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static string Day(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
