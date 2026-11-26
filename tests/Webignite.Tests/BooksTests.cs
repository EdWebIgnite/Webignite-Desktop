using Webignite.Core.Books;
using Webignite.Core.Finance;

namespace Webignite.Tests;

/// <summary>The books under the money pages: double entry the accountant can work from, with plain names for Eddie.</summary>
public sealed class BooksTests
{
    private static readonly IReadOnlyList<Account> Chart = ChartOfAccounts.Default;
    private static readonly DateOnly Day = new(2026, 11, 25);

    private static JournalEntry Entry(string id, DateOnly date, params JournalLine[] lines) =>
        new() { Id = id, Date = date, Memo = id, Lines = lines };

    [Fact]
    public void The_default_chart_has_one_account_for_each_job_the_app_posts_to_and_no_VAT()
    {
        foreach (var role in new[] { AccountRole.Bank, AccountRole.Debtors, AccountRole.Creditors, AccountRole.DirectorsLoan, AccountRole.ShareCapital, AccountRole.Dividends, AccountRole.RetainedEarnings })
            Assert.Single(Chart, a => a.Role == role);
        Assert.Equal(Chart.Count, Chart.Select(a => a.Code).Distinct().Count());
        Assert.DoesNotContain(Chart, a => a.Name.Contains("VAT", StringComparison.OrdinalIgnoreCase));
        // Every expense category on the money page lands on an account that exists.
        Assert.All(ExpenseCategories.All, c => Assert.Contains(Chart, a => a.Code == ChartOfAccounts.ForExpenseCategory(c)));
    }

    [Fact]
    public void A_journal_must_balance_and_use_accounts_that_exist()
    {
        Assert.Null(Journals.Problem(Entry("ok", Day, JournalLine.Dr("1000", 100), JournalLine.Cr("4000", 100)), Chart));
        Assert.Contains("must be the same", Journals.Problem(Entry("off", Day, JournalLine.Dr("1000", 100), JournalLine.Cr("4000", 90)), Chart));
        Assert.Contains("no account 9999", Journals.Problem(Entry("x", Day, JournalLine.Dr("9999", 100), JournalLine.Cr("4000", 100)), Chart));
        Assert.NotNull(Journals.Problem(Entry("one", Day, JournalLine.Dr("1000", 100)), Chart));
        Assert.NotNull(Journals.Problem(Entry("zero", Day, JournalLine.Dr("1000", 0), JournalLine.Cr("4000", 0)), Chart));
    }

    [Fact]
    public void Money_typed_in_on_the_money_page_goes_through_the_bank_to_the_right_account()
    {
        var hosting = Journals.FromMoneyEntry(new MoneyEntry { Id = "e1", Kind = EntryKind.Expense, Date = Day, Amount = 450m, Category = "Hosting and servers", Party = "Hetzner" }, Chart);
        Assert.Null(Journals.Problem(hosting, Chart));
        Assert.Equal([("6000", 450m, 0m), ("1000", 0m, 450m)], hosting.Lines.Select(l => (l.Account, l.Debit, l.Credit)));
        Assert.Equal("Money out, Hetzner", hosting.Memo);

        var came = Journals.FromMoneyEntry(new MoneyEntry { Id = "e2", Kind = EntryKind.Received, Date = Day, Amount = 1200m }, Chart);
        Assert.Equal([("1000", 1200m, 0m), ("4900", 0m, 1200m)], came.Lines.Select(l => (l.Account, l.Debit, l.Credit)));
    }

    [Fact]
    public void The_reports_agree_with_each_other()
    {
        JournalEntry[] entries =
        [
            Entry("capital", new(2026, 1, 10), JournalLine.Dr("1000", 10000m), JournalLine.Cr("3000", 10000m)),
            // Last tax year: a sale and an expense, so R 3 000 of earnings carried into this one.
            Entry("sale-old", new(2026, 2, 15), JournalLine.Dr("1000", 5000m), JournalLine.Cr("4000", 5000m)),
            Entry("cost-old", new(2026, 2, 20), JournalLine.Dr("6020", 2000m), JournalLine.Cr("1000", 2000m)),
            // This tax year: an invoice still owed, a hosting sale paid, an expense on the card.
            Entry("invoice", new(2026, 4, 1), JournalLine.Dr("1100", 8000m), JournalLine.Cr("4000", 8000m)),
            Entry("hosting", new(2026, 5, 1), JournalLine.Dr("1000", 1500m), JournalLine.Cr("4100", 1500m)),
            Entry("card", new(2026, 6, 1), JournalLine.Dr("6000", 500m), JournalLine.Cr("2100", 500m)),
            Entry("dividend", new(2026, 7, 1), JournalLine.Dr("3100", 1000m), JournalLine.Cr("1000", 1000m)),
        ];

        var trial = BookReports.Trial(Chart, entries, Day);
        Assert.True(trial.Balances);
        Assert.Equal(25000m, trial.TotalDebit); // bank 13 500, debtors 8 000, software 2 000, hosting 500, dividends 1 000

        var profit = BookReports.Profit(Chart, entries, TaxYears.Start(Day), Day);
        Assert.Equal(9500m, profit.TotalIncome);
        Assert.Equal(500m, profit.TotalExpenses);
        Assert.Equal(9000m, profit.Profit);

        var sheet = BookReports.Balances(Chart, entries, Day);
        Assert.True(sheet.Balances);
        Assert.Equal(13500m + 8000m, sheet.TotalAssets); // bank 13 500, debtors 8 000
        Assert.Equal(500m, sheet.TotalLiabilities);
        Assert.Equal(9000m, sheet.ProfitThisYear);
        Assert.Equal(3000m, sheet.Equity.Single(e => e.Account.Role == AccountRole.RetainedEarnings).Amount);
        Assert.Equal(-1000m, sheet.Equity.Single(e => e.Account.Role == AccountRole.Dividends).Amount);
    }

    [Theory]
    [InlineData("2026-03-01", "2026-03-01", "2027-02-28", "2027 tax year")]
    [InlineData("2026-02-28", "2025-03-01", "2026-02-28", "2026 tax year")]
    [InlineData("2027-12-01", "2027-03-01", "2028-02-29", "2028 tax year")]
    public void The_tax_year_runs_from_March_to_February(string day, string start, string end, string name)
    {
        var d = DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), TaxYears.Start(d));
        Assert.Equal(DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), TaxYears.End(d));
        Assert.Equal(name, TaxYears.Name(d));
    }
}
