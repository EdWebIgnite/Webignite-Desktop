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

        if (Emailed(stored) is { } emailed)
            parts.Add(emailed);

        for (var i = 1; i < parts.Count; i++)
            parts[i] = char.ToLowerInvariant(parts[i][0]) + parts[i][1..];
        return string.Join(", ", parts);
    }

    /// <summary>
    /// "Emailed to Thandi Mokoena 3 Nov" or, for mail sent before names were kept, "Emailed 3 Nov". Null when it
    /// hasn't been emailed from Webignite.
    /// </summary>
    public static string? Emailed(StoredDocument stored) => stored.Emailed is not { } when ? null
        : string.IsNullOrWhiteSpace(stored.EmailedTo) ? $"Emailed {Short(when)}"
        : $"Emailed to {stored.EmailedTo.Trim()} {Short(when)}";

    private static string Short(DateTimeOffset when) => when.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture);

    /// <summary>
    /// The line under a short list: the most useful next step. Overdue money first, then what is still on this
    /// PC, then drafts waiting for approval in Xero, then accepted quotes with no invoice yet. With nothing to
    /// sort out it offers a new one. Returns null when there are none at all; the empty state covers that.
    /// </summary>
    public static DocumentHint? Hint(DocumentKind kind, IReadOnlyCollection<StoredDocument> documents, DateOnly today)
    {
        var mine = documents.Where(d => d.Document.Kind == kind).ToList();
        if (mine.Count == 0)
            return null;
        var noun = kind == DocumentKind.Quote ? "quote" : "invoice";

        var overdue = mine.Where(d => Receivables.IsOverdue(d, today)).ToList();
        if (overdue.Count > 0)
        {
            var amount = Money.Rand(overdue.Sum(Receivables.Outstanding));
            return new(overdue.Count == 1
                ? $"1 invoice is overdue, {amount}."
                : $"{overdue.Count} invoices are overdue, {amount} in all.", "Show overdue", DocumentShow.Overdue);
        }

        var onThisPc = mine.Count(d => d.Status != DocumentStatus.InXero);
        if (onThisPc > 0)
            return new(onThisPc == 1
                ? $"1 {noun} is still on this PC. Open it and send it to Xero as a draft when it's ready."
                : $"{onThisPc} {noun}s are still on this PC. Open one and send it to Xero as a draft when it's ready.",
                onThisPc == 1 ? "Show it" : "Show them", DocumentShow.NotInXero);

        var drafts = mine.Count(d => d.Settled is null && d.XeroStatus is "DRAFT" or "SUBMITTED");
        if (drafts > 0)
            return new(drafts == 1
                ? $"1 {noun} is a draft in Xero. Approve it in Xero when it can go to the client."
                : $"{drafts} {noun}s are drafts in Xero. Approve them in Xero when they can go to the client.", null, null);

        var accepted = mine.Count(d => d.SettledAs == "accepted");
        if (kind == DocumentKind.Quote && accepted > 0)
            return new(accepted == 1
                ? "1 accepted quote has no invoice yet. Open it and pick Make invoice."
                : $"{accepted} accepted quotes have no invoice yet. Open one and pick Make invoice.",
                accepted == 1 ? "Show it" : "Show them", DocumentShow.Settled);

        return kind == DocumentKind.Quote
            ? new("Nothing to sort out. Quotes waiting on a client turn up on To-do when it's time to follow up.", "New quote", null)
            : new("Nothing to sort out. Every invoice is in Xero and none is overdue.", "New invoice", null);
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static string Day(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}

/// <summary>The line under a short quote or invoice list. With an action, a null filter means "start a new one".</summary>
public sealed record DocumentHint(string Text, string? ActionText, DocumentShow? Show);
