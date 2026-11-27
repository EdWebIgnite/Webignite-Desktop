using Webignite.Core.Documents;

namespace Webignite.Core.Clients;

/// <summary>Where a client stands with money, for the top of their page.</summary>
public sealed record ClientMoneyFigures(decimal Owed, decimal Overdue, decimal PaidThisYear, decimal InvoicedThisYear, int? DaysToPay);

/// <summary>
/// A client's money at a glance, from their quotes and invoices: what they owe and how much of it is late, what they
/// were invoiced and paid this tax year, and how long they usually take to pay. Counts invoices in Xero and ones made
/// in Webignite alike.
/// </summary>
public static class ClientMoney
{
    public static ClientMoneyFigures For(IEnumerable<StoredDocument> theirs, DateOnly today)
    {
        var invoices = theirs.Where(d => d.Document.Kind == DocumentKind.Invoice).ToList();
        var (from, to) = Dates.TaxYear(today);
        bool InYear(DateOnly? day) => day is { } d && d >= from && d <= to;

        var counted = invoices.Where(d => (d.Status == DocumentStatus.Issued || d.Status == DocumentStatus.InXero && !XeroStatuses.IsDraft(d.Xero.Status))
            && !Settlements.IsCancelled(d.Settled?.As)).ToList();
        var invoiced = counted.Where(d => InYear(d.Document.Date)).Sum(d => d.Document.Total ?? 0);

        decimal paid = 0;
        foreach (var d in counted)
        {
            if (d.Own is { } own)
                paid += own.Payments.Where(p => InYear(p.Date)).Sum(p => p.Amount);
            else if (d.Settled?.As == Settlement.Paid && InYear(d.Xero.PaidOn))
                paid += d.Xero.AmountPaid ?? d.Document.Total ?? 0;
            else if (d.Settled is null && d.Xero.AmountPaid is > 0 && InYear(d.Document.Date))
                paid += d.Xero.AmountPaid.Value;
        }

        // How long they take: the last twelve invoices paid in full, from the invoice date to the day it was paid.
        var days = counted.Where(d => d.Settled?.As == Settlement.Paid && d.Xero.PaidOn is not null && d.Document.Date is not null)
            .OrderByDescending(d => d.Xero.PaidOn).Take(12)
            .Select(d => Math.Max(0, d.Xero.PaidOn!.Value.DayNumber - d.Document.Date!.Value.DayNumber)).ToList();

        return new ClientMoneyFigures(Receivables.Owed(invoices), Receivables.Overdue(invoices, today), Money.Cents(paid), Money.Cents(invoiced),
            days.Count == 0 ? null : (int)Math.Round(days.Average(), MidpointRounding.AwayFromZero));
    }

    /// <summary>"Usually pays in 12 days", "Usually pays on the day", or empty before anything was paid.</summary>
    public static string PaysIn(int? days) => days switch
    {
        null => "",
        0 => "Usually pays on the day",
        1 => "Usually pays in a day",
        { } d => $"Usually pays in {d} days",
    };
}
