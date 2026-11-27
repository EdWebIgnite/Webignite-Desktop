using Webignite.Core.Books;
using Webignite.Core.Documents;

namespace Webignite.Core.Finance;

/// <summary>
/// A supplier's bill kept in Webignite (when quotes and invoices are made here instead of Xero): owed from its date
/// until it is paid. Paying it adds the expense to the money page, so the money that went out is in one place.
/// </summary>
public sealed record SupplierBill
{
    public required string Id { get; init; }
    public required string Supplier { get; init; }

    /// <summary>The supplier's own number for it, e.g. INV-7781.</summary>
    public string? Number { get; init; }

    public required DateOnly Date { get; init; }
    public DateOnly? Due { get; init; }
    public required decimal Total { get; init; }

    /// <summary>One of <see cref="ExpenseCategories.All"/>; it says which expense account the books use.</summary>
    public string Category { get; init; } = "Other";

    public string Description { get; init; } = "";

    /// <summary>The bill itself (the supplier's PDF or a photo), kept in the receipts folder under the bill's id.</summary>
    public EntryReceipt? Receipt { get; init; }

    /// <summary>The day it was paid, once it was.</summary>
    public DateOnly? Paid { get; init; }

    /// <summary>The expense on the money page made when it was paid.</summary>
    public string? PaidEntryId { get; init; }

    public DateTimeOffset Created { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

public static class SupplierBills
{
    /// <summary>What is wrong with a bill before it's kept, or null.</summary>
    public static string? Check(SupplierBill bill) =>
        string.IsNullOrWhiteSpace(bill.Supplier) ? "Type who the bill is from."
        : bill.Total <= 0 ? "Type the amount to pay."
        : bill.Due is { } due && due < bill.Date ? "The due date is before the bill's date."
        : null;

    public static bool IsOwed(SupplierBill bill) => bill.Paid is null;

    public static bool IsOverdue(SupplierBill bill, DateOnly today) => IsOwed(bill) && bill.Due is { } due && due < today;

    public static decimal Owed(IEnumerable<SupplierBill> bills) => Money.Cents(bills.Where(IsOwed).Sum(b => b.Total));

    /// <summary>Still to pay, overdue first then by due day; paid ones after, the latest first.</summary>
    public static IReadOnlyList<SupplierBill> InOrder(IEnumerable<SupplierBill> bills) =>
        [.. bills.OrderBy(b => IsOwed(b) ? 0 : 1).ThenBy(b => IsOwed(b) ? b.Due ?? b.Date : DateOnly.MaxValue)
            .ThenByDescending(b => b.Paid).ThenBy(b => b.Supplier, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>One line for the money page: "R 3 049.00 owed on 2 bills, R 1 899.00 overdue."</summary>
    public static string Summary(IReadOnlyList<SupplierBill> bills, DateOnly today)
    {
        var owed = bills.Where(IsOwed).ToList();
        if (owed.Count == 0)
            return "No bills to pay.";
        var line = $"{Money.Rand(Owed(owed))} owed on {owed.Count} {(owed.Count == 1 ? "bill" : "bills")}";
        var late = Money.Cents(owed.Where(b => IsOverdue(b, today)).Sum(b => b.Total));
        return late > 0 ? $"{line}, {Money.Rand(late)} overdue." : line + ".";
    }

    /// <summary>
    /// The bill paid in full on a day, and the expense that goes on the money page for it (linked back, so the books
    /// count the bill and its payment once). Null when it was already paid.
    /// </summary>
    public static (SupplierBill Bill, MoneyEntry Expense)? Pay(SupplierBill bill, DateOnly day, DateTimeOffset now)
    {
        if (!IsOwed(bill))
            return null;
        var expense = new MoneyEntry
        {
            Id = MoneyEntry.NewId(), Kind = EntryKind.Expense, Date = day, Amount = bill.Total, Party = bill.Supplier,
            Category = bill.Category, Note = bill.Number is { } n ? $"Bill {n}" : null, BillId = bill.Id, Receipt = bill.Receipt, Created = now,
        };
        return (bill with { Paid = day, PaidEntryId = expense.Id }, expense);
    }

    /// <summary>
    /// The books for a bill: owed to the supplier on its date (the expense, against creditors), then paid from the
    /// bank on the day it was paid.
    /// </summary>
    public static IReadOnlyList<JournalEntry> Postings(SupplierBill bill, IReadOnlyCollection<Account> chart)
    {
        var creditors = ChartOfAccounts.WithRole(chart, AccountRole.Creditors)!.Code;
        var bank = ChartOfAccounts.WithRole(chart, AccountRole.Bank)!.Code;
        var what = bill.Number is { } n ? $"Bill {n}, {bill.Supplier}" : $"Bill from {bill.Supplier}";
        var entries = new List<JournalEntry>
        {
            new()
            {
                Id = "bill-" + bill.Id, Date = bill.Date, Memo = what, Source = JournalSource.Bill, SourceId = bill.Id,
                Lines = [JournalLine.Dr(ChartOfAccounts.ForExpenseCategory(bill.Category), bill.Total, bill.Supplier), JournalLine.Cr(creditors, bill.Total, bill.Supplier)],
            },
        };
        if (bill.Paid is { } paid)
            entries.Add(new JournalEntry
            {
                Id = "billpaid-" + bill.Id, Date = paid, Memo = "Paid: " + what, Source = JournalSource.BillPayment, SourceId = bill.Id,
                Lines = [JournalLine.Dr(creditors, bill.Total, bill.Supplier), JournalLine.Cr(bank, bill.Total, bill.Supplier)],
            });
        return entries;
    }
}
