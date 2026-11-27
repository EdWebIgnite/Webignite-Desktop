using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// Bringing in a bank statement: the lines not brought in before, each with what it most likely is, and then what
/// the person confirmed, recorded where it belongs. Nothing is recorded without their yes.
/// </summary>
public sealed class BankImport(LocalStore store, DocumentService documents)
{
    /// <summary>The statement's new lines, newest first, each with its suggested match.</summary>
    public IReadOnlyList<(BankLine Line, BankMatch Match)> Prepare(BankStatement statement, BankSettings settings)
    {
        var fresh = BankStatements.New(statement.Lines, settings.Seen.ToHashSet(StringComparer.Ordinal));
        if (fresh.Count == 0)
            return [];
        var from = fresh.Min(l => l.Date).AddDays(-BankMatching.DaysApart);
        var to = fresh.Max(l => l.Date).AddDays(BankMatching.DaysApart);
        var entries = store.ListEntries(from, to);
        var invoices = documents.List().Where(d => d.Document.Kind == DocumentKind.Invoice).ToList();
        var bills = store.ListSupplierBills();
        return [.. fresh.OrderByDescending(l => l.Date).Select(l => (l, BankMatching.Suggest(l, invoices, bills, entries, settings.Rules)))];
    }

    /// <summary>
    /// Records one confirmed line: an invoice paid (its payment on the invoice when it was made here, else money
    /// received naming it), a bill paid, or new money in or out. Returns what was done, in a few words.
    /// </summary>
    public string Apply(BankLine line, BankMatch match, string? category, string? party, DateTimeOffset now)
    {
        var amount = Math.Abs(line.Amount);
        switch (match.Kind)
        {
            case BankMatchKind.Invoice when match.Id is { } id && documents.Get(id) is { } invoice:
            {
                var number = OwnInvoicing.NumberOf(invoice);
                string? documentId = null;
                if (invoice.Status == DocumentStatus.Issued)
                {
                    var paying = Math.Min(amount, OwnInvoicing.Due(invoice));
                    if (paying > 0 && documents.Change(id, s => OwnInvoicing.Record(s, paying, line.Date, "Bank", now)) is not null)
                        documentId = id;
                }
                store.SaveEntry(new MoneyEntry
                {
                    Id = MoneyEntry.NewId(), Kind = EntryKind.Received, Date = line.Date, Amount = amount, Party = invoice.Document.Customer.Name,
                    Note = number, DocumentId = documentId, Created = now,
                });
                return $"{number} paid";
            }
            case BankMatchKind.Bill when match.Id is { } id && store.GetSupplierBill(id) is { } bill && SupplierBills.Pay(bill, line.Date, now) is { } paid:
                store.SavePaidBill(paid.Bill, paid.Expense);
                return $"Bill from {bill.Supplier} paid";
            case BankMatchKind.AlreadyEntered:
                return "Already in";
            default:
                store.SaveEntry(new MoneyEntry
                {
                    Id = MoneyEntry.NewId(), Kind = line.IsIn ? EntryKind.Received : EntryKind.Expense, Date = line.Date, Amount = amount,
                    Party = string.IsNullOrWhiteSpace(party) ? null : party.Trim(), Category = line.IsIn ? null : category ?? "Other",
                    Note = line.Description.Length > 0 ? line.Description : null, Created = now,
                });
                return line.IsIn ? "Money in added" : "Expense added";
        }
    }
}
