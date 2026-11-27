using System.Globalization;

namespace Webignite.Core.Documents;

/// <summary>A figure at the top of the quote or invoice list: what it's about, the amount, how many, and the list it opens.</summary>
public sealed record DocumentTile(string Label, string Amount, string Count, DocumentShow Show, bool Problem = false);

/// <summary>
/// The three figures at the top of the quote and invoice lists, so where things stand is seen at a glance and one
/// click shows the ones behind it. Invoices: owed, overdue and paid this month. Quotes: waiting on clients,
/// accepted this month and drafts not sent yet.
/// </summary>
public static class DocumentTiles
{
    public static IReadOnlyList<DocumentTile> For(DocumentKind kind, IReadOnlyCollection<StoredDocument> documents, DateOnly today)
    {
        var mine = documents.Where(d => d.Document.Kind == kind).ToList();
        var month = today.ToString("MMMM", CultureInfo.CurrentCulture);
        if (kind == DocumentKind.Invoice)
        {
            var owing = mine.Where(d => Receivables.Outstanding(d) > 0).ToList();
            var overdue = owing.Where(d => Receivables.IsOverdue(d, today)).ToList();
            var paid = mine.Where(d => d.Settled?.As == Settlement.Paid && Receivables.PaidOn(d) is { } on && Dates.MonthStart(on) == Dates.MonthStart(today)).ToList();
            return
            [
                new("Owed to you", Money.Rand(Receivables.Owed(owing)), Count(owing.Count, "invoice"), DocumentShow.Owing),
                new("Overdue", Money.Rand(Receivables.Overdue(overdue, today)), Count(overdue.Count, "invoice"), DocumentShow.Overdue, Problem: overdue.Count > 0),
                new($"Paid in {month}", Money.Rand(paid.Sum(d => d.Document.Total ?? 0)), Count(paid.Count, "invoice"), DocumentShow.Settled),
            ];
        }
        var waiting = mine.Where(d => Receivables.IsOpenQuote(d, today)).ToList();
        var accepted = mine.Where(d => d.Settled?.As is Settlement.Accepted or Settlement.Invoiced && d.Settled.On is var at
            && Dates.MonthStart(DateOnly.FromDateTime(at.ToLocalTime().DateTime)) == Dates.MonthStart(today)).ToList();
        var drafts = mine.Where(d => d.Status is DocumentStatus.Draft or DocumentStatus.AwaitingApproval
            || (d.Settled is null && d.Delivery.Emailed is null && d.Own.Sent is null && d.Xero.Status != XeroStatuses.Sent)).ToList();
        return
        [
            new("Waiting on clients", Money.Rand(waiting.Sum(d => d.Document.Total ?? 0)), Count(waiting.Count, "quote"), DocumentShow.Waiting),
            new($"Accepted in {month}", Money.Rand(accepted.Sum(d => d.Document.Total ?? 0)), Count(accepted.Count, "quote"), DocumentShow.Settled),
            new("Not sent yet", Money.Rand(drafts.Sum(d => d.Document.Total ?? 0)), Count(drafts.Count, "quote"), DocumentShow.NotInXero),
        ];
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : string.Create(CultureInfo.InvariantCulture, $"{n} {noun}s");
}
