namespace Webignite.Core.Documents;

/// <summary>
/// Recording a payment against an invoice in Xero, only when Eddie clicks to: all or part of what's owed, on a
/// day, into one of Xero's bank accounts. Assistants never record payments.
/// </summary>
public static class InvoicePayments
{
    /// <summary>What's still owed as last read from Xero, else as the document says.</summary>
    public static decimal Due(StoredDocument s) => s.Xero.AmountDue ?? s.Document.BalanceDue ?? s.Document.Total ?? 0;

    /// <summary>An approved invoice in Xero, not settled, with something still owed.</summary>
    public static bool CanRecord(StoredDocument s) =>
        s.Document.Kind == DocumentKind.Invoice
        && s.Xero.Id is not null
        && s.Xero.State is Xero.XeroWriteState.Verified or Xero.XeroWriteState.Mismatch
        && !InvoiceCorrections.IsXeroDraft(s)
        && s.Settled is null
        && Due(s) > 0;

    /// <summary>Why the payment can't be recorded as typed, or null when it can.</summary>
    public static string? Check(StoredDocument s, decimal? amount, DateOnly? date, string? accountId, DateOnly today) =>
        amount is null ? "Type the amount paid, like 500 or 1 250.50."
        : amount <= 0 ? "Type an amount above R 0."
        : amount > Due(s) ? $"That's more than the {Money.Rand(Due(s))} still owed."
        : date is null ? "Pick the day it was paid."
        : date > today ? "The day it was paid can't be in the future."
        : string.IsNullOrEmpty(accountId) ? "Pick the bank account it was paid into."
        : null;
}
