namespace Webignite.Core.Documents;

/// <summary>
/// Quotes and invoices finished in Webignite without Xero: given their number, sent, paid and answered here. What
/// is owed and what is overdue follow from the payments recorded here, as they follow from Xero for the others.
/// </summary>
public static class OwnDocuments
{
    /// <summary>Finished here, not in Xero.</summary>
    public static bool IsOwn(StoredDocument s) => s.Status == DocumentStatus.Issued;

    /// <summary>A draft that can be finished here: not in Xero, nothing waiting on approval, everything typed in.</summary>
    public static bool CanIssue(StoredDocument s) =>
        s.Status is DocumentStatus.Draft or DocumentStatus.AwaitingApproval && s.Xero is { Id: null, State: Xero.XeroWriteState.None } && s.Document.Origin == DocumentOrigin.New;

    /// <summary>The draft finished with this number: it is the client's document from now on.</summary>
    public static StoredDocument Issue(StoredDocument s, string number, DateTimeOffset now) => s with
    {
        Status = DocumentStatus.Issued,
        Document = s.Document with { Number = number.Trim() },
        Own = s.Own with { Issued = now },
    };

    /// <summary>Back to a draft to change it, keeping its number: only while the client hasn't had it and nothing is paid.</summary>
    public static bool CanReopen(StoredDocument s) =>
        IsOwn(s) && s.Delivery.Emailed is null && s.Own.Sent is null && s.Own.Payments.Count == 0 && s.Settled is null;

    public static StoredDocument Reopen(StoredDocument s) => s with { Status = DocumentStatus.Draft, Own = s.Own with { Issued = null } };

    /// <summary>The client has it: emailed from here, or marked as given another way.</summary>
    public static bool IsSent(StoredDocument s) => s.Delivery.Emailed is not null || s.Own.Sent is not null;

    /// <summary>Emailed from here to these people.</summary>
    public static StoredDocument Emailed(StoredDocument s, string? to, DateTimeOffset now) =>
        s with { Delivery = s.Delivery with { Emailed = now, EmailedTo = to ?? s.Delivery.EmailedTo } };

    public static bool CanMarkSent(StoredDocument s) => IsOwn(s) && !IsSent(s) && s.Settled is null;

    /// <summary>Given to the client some other way: by hand, WhatsApp, or long ago.</summary>
    public static StoredDocument MarkSent(StoredDocument s, DateTimeOffset now) => s with { Own = s.Own with { Sent = now } };

    public static decimal Total(StoredDocument s) => s.Document.Total ?? Money.Cents(s.Document.NetSubtotal() + (s.Document.TaxTotal ?? 0));

    public static decimal Paid(StoredDocument s) => Money.Cents(s.Own.Payments.Sum(p => p.Amount));

    public static decimal Due(StoredDocument s) => Math.Max(0, Money.Cents(Total(s) - Paid(s)));

    /// <summary>An invoice finished here, not cancelled, with something still owed.</summary>
    public static bool CanPay(StoredDocument s) =>
        IsOwn(s) && s.Document.Kind == DocumentKind.Invoice && !Settlements.IsCancelled(s.Settled?.As) && Due(s) > 0;

    /// <summary>Why the payment can't be recorded as typed, or null when it can.</summary>
    public static string? PaymentProblem(StoredDocument s, decimal? amount, DateOnly? day, DateOnly today) =>
        amount is null ? "Type the amount paid, like 500 or 1 250.50."
        : amount <= 0 ? "Type an amount above R 0."
        : Money.Cents(amount.Value) > Due(s) ? $"That's more than the {Money.Rand(Due(s))} still owed."
        : day is null ? "Pick the day it was paid."
        : day > today ? "The day it was paid can't be in the future."
        : null;

    /// <summary>The payment recorded; once nothing is owed the invoice is paid.</summary>
    public static StoredDocument Pay(StoredDocument s, decimal amount, DateOnly day, DateTimeOffset now, string? note = null)
    {
        var paid = s with { Own = s.Own with { Payments = [.. s.Own.Payments, new OwnPayment(Money.Cents(amount), day, string.IsNullOrWhiteSpace(note) ? null : note.Trim())] } };
        return Due(paid) == 0 ? paid with { Settled = new SettledState { On = now, As = Settlement.Paid } } : paid;
    }

    /// <summary>The last payment taken off again, for a mistake; a paid invoice is owed again.</summary>
    public static StoredDocument Unpay(StoredDocument s) => s.Own.Payments.Count == 0 ? s : s with
    {
        Own = s.Own with { Payments = [.. s.Own.Payments.SkipLast(1)] },
        Settled = s.Settled?.As == Settlement.Paid ? null : s.Settled,
    };

    /// <summary>The day it was paid in full, when it was.</summary>
    public static DateOnly? PaidOn(StoredDocument s) => s.Settled?.As == Settlement.Paid && s.Own.Payments.Count > 0 ? s.Own.Payments.Max(p => p.On) : null;

    /// <summary>A quote the client has, with no answer yet.</summary>
    public static bool CanDecide(StoredDocument s) =>
        IsOwn(s) && s.Document.Kind == DocumentKind.Quote && s.Settled is null && IsSent(s);

    public static StoredDocument Decide(StoredDocument s, bool accepted, DateTimeOffset now) =>
        s with { Settled = new SettledState { On = now, As = accepted ? Settlement.Accepted : Settlement.Declined } };

    /// <summary>An invoice finished here with nothing paid on it can be cancelled; its number stays used.</summary>
    public static bool CanCancel(StoredDocument s) =>
        IsOwn(s) && s.Document.Kind == DocumentKind.Invoice && s.Settled is null && s.Own.Payments.Count == 0;

    public static StoredDocument Cancel(StoredDocument s, DateTimeOffset now) => s with { Settled = new SettledState { On = now, As = Settlement.Voided } };

    /// <summary>Where it stands, in a sentence for the editor.</summary>
    public static string Standing(StoredDocument s, DateOnly today)
    {
        var number = s.Number ?? "It";
        var what = s.Document.Kind == DocumentKind.Quote ? "quote" : "invoice";
        return s.Settled?.As switch
        {
            Settlement.Paid => PaidOn(s) is { } on ? $"{number} was paid on {on.ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture)}." : $"{number} is paid.",
            Settlement.Voided => $"{number} is cancelled. Its number stays used.",
            Settlement.Accepted => $"The client accepted {number}.",
            Settlement.Declined => $"The client declined {number}.",
            Settlement.Invoiced => $"{number} has been invoiced.",
            _ when Paid(s) > 0 => $"{Money.Rand(Paid(s))} paid on {number}. {Money.Rand(Due(s))} is still owed.",
            _ when IsSent(s) => $"Finished as {number} and with the client.",
            _ => $"Finished as {number}. Email it, or mark it sent if the client got it another way. Change it puts it back to a draft.",
        } + (s.Document.Kind == DocumentKind.Invoice && s.Settled is null && Due(s) > 0 && s.Document.DueOrExpiryDate < today ? " It is overdue." : "")
          + (s.Settled is null && s.Document.Kind == DocumentKind.Quote && s.Document.DueOrExpiryDate < today ? $" The {what} has expired." : "");
    }
}
