using Webignite.Core.Xero;

namespace Webignite.Core.Documents;

/// <summary>
/// Putting an invoice right once it's in Xero: a credit note against it, drafted for Eddie to approve in Xero, or
/// voiding it (deleting it while it's still a draft), only on Eddie's own click. Assistants do neither.
/// </summary>
public static class InvoiceCorrections
{
    private static bool InXero(StoredDocument s) =>
        s.Document.Kind == DocumentKind.Invoice && s.Xero.Id is not null && s.Xero.State is XeroWriteState.Verified or XeroWriteState.Mismatch;

    /// <summary>Not approved in Xero yet, so it is deleted rather than voided.</summary>
    public static bool IsXeroDraft(StoredDocument s) =>
        s.Delivery.Approved is null && (s.Xero.Status is null || XeroStatuses.IsDraft(s.Xero.Status));

    /// <summary>In Xero, not settled and with nothing paid: Xero only voids an invoice without payments.</summary>
    public static bool CanVoid(StoredDocument s) =>
        InXero(s) && s.Settled is null && (s.Xero.AmountPaid ?? s.Document.AmountPaid ?? 0) == 0;

    public static string VoidLabel(StoredDocument s) => IsXeroDraft(s) ? "Delete the draft in Xero" : "Void in Xero";

    public static string VoidQuestion(StoredDocument s) => IsXeroDraft(s)
        ? $"Delete draft {s.Xero.Number} in Xero? It goes from Xero and can't be brought back."
        : $"Void {s.Xero.Number} in Xero? It stays in Xero marked void, no longer owed, and can't be undone. The customer isn't told; let them know yourself if they had it.";

    /// <summary>What's left to credit: the total less credit notes already drafted from here.</summary>
    public static decimal Creditable(StoredDocument s) => Math.Max(0, (s.Document.Total ?? 0) - s.CreditNotes.Sum(c => c.Amount));

    /// <summary>Approved in Xero and not voided or deleted, with something left to credit. Paid ones can be credited (a refund).</summary>
    public static bool CanCredit(StoredDocument s) =>
        InXero(s) && !IsXeroDraft(s) && s.Settled?.As is not (Settlement.Voided or Settlement.Deleted) && Creditable(s) > 0;

    /// <summary>Why the amount typed can't be credited, or null when it can. No amount means all that's left.</summary>
    public static string? CheckAmount(StoredDocument s, decimal? amount) =>
        amount is null ? null
        : amount <= 0 ? "Type an amount above R 0, or leave it empty to credit all of it."
        : amount > Creditable(s) ? $"That's more than the {Money.Rand(Creditable(s))} left to credit."
        : null;

    /// <summary>The whole invoice is credited when no amount is given or the amount is all that's left.</summary>
    public static bool IsFull(StoredDocument s, decimal? amount) =>
        s.CreditNotes.Count == 0 && (amount is null || amount == Creditable(s));

    /// <summary>One line for each credit note, for the invoice's page.</summary>
    public static IReadOnlyList<string> Describe(StoredDocument s, TimeZoneInfo zone) =>
        s.CreditNotes.Select(c =>
            $"Credit note {c.Number ?? "(no number yet)"} for {Money.Rand(c.Amount)}, drafted in Xero on "
            + TimeZoneInfo.ConvertTime(c.Drafted, zone).ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture)
            + ". Approve it in Xero and allocate it to this invoice.").ToList();
}
