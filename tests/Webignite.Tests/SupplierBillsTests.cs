using Webignite.Core.Books;
using Webignite.Core.Finance;

namespace Webignite.Tests;

public sealed class SupplierBillsTests
{
    private static readonly IReadOnlyList<Account> Chart = ChartOfAccounts.Default;
    private static readonly DateOnly Today = new(2026, 11, 27);
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 10, 0, 0, TimeSpan.FromHours(2));

    private static SupplierBill Bill(string supplier, decimal total, DateOnly? due = null, string category = "Hosting and servers") => new()
    {
        Id = SupplierBill.NewId(), Supplier = supplier, Number = "INV-7781", Date = Today.AddDays(-10), Due = due, Total = total, Category = category,
    };

    [Fact]
    public void Bills_owed_and_overdue_are_summed_in_one_line()
    {
        var bills = new[] { Bill("xneelo", 1899m, Today.AddDays(-3)), Bill("Adobe", 1150m, Today.AddDays(5)) };
        Assert.Equal("R 3 049.00 owed on 2 bills, R 1 899.00 overdue.", SupplierBills.Summary(bills, Today));
        Assert.Equal("No bills to pay.", SupplierBills.Summary([], Today));
        Assert.Equal(["xneelo", "Adobe"], SupplierBills.InOrder(bills.Reverse()).Select(b => b.Supplier));
    }

    [Fact]
    public void A_bill_needs_a_supplier_an_amount_and_a_due_date_after_its_date()
    {
        Assert.NotNull(SupplierBills.Check(Bill("", 10m)));
        Assert.NotNull(SupplierBills.Check(Bill("xneelo", 0m)));
        Assert.NotNull(SupplierBills.Check(Bill("xneelo", 10m, Today.AddDays(-20))));
        Assert.Null(SupplierBills.Check(Bill("xneelo", 10m, Today)));
    }

    [Fact]
    public void Paying_a_bill_adds_its_expense_once_and_the_books_balance()
    {
        var bill = Bill("xneelo", 1899m) with { Receipt = new EntryReceipt("xneelo.pdf", 900) };
        var (paid, expense) = SupplierBills.Pay(bill, Today, Now)!.Value;

        Assert.Equal(Today, paid.Paid);
        Assert.Equal(expense.Id, paid.PaidEntryId);
        Assert.Equal((EntryKind.Expense, 1899m, "xneelo", bill.Id, "xneelo.pdf"), (expense.Kind, expense.Amount, expense.Party, expense.BillId, expense.Receipt?.FileName));
        Assert.Null(SupplierBills.Pay(paid, Today, Now));

        var books = Postings.Books([], [expense], [paid], Chart);
        decimal Held(AccountRole role) => BookReports.Balance(Chart.Single(a => a.Role == role), books, DateOnly.MinValue, Today);
        Assert.Equal(0m, Held(AccountRole.Creditors));
        Assert.Equal(-1899m, Held(AccountRole.Bank));
        Assert.Equal(1899m, BookReports.Balance(Chart.Single(a => a.Code == ChartOfAccounts.ForExpenseCategory("Hosting and servers")), books, DateOnly.MinValue, Today));
        Assert.All(books, e => Assert.Null(Journals.Problem(e, Chart)));
    }

    [Fact]
    public void A_bill_not_yet_paid_is_owed_to_the_supplier_in_the_books()
    {
        var books = Postings.Books([], [], [Bill("Adobe", 1150m, category: "Software and subscriptions")], Chart);
        Assert.Equal(1150m, BookReports.Balance(Chart.Single(a => a.Role == AccountRole.Creditors), books, DateOnly.MinValue, Today));
    }
}
