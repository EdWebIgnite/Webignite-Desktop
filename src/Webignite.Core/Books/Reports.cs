using Webignite.Core.Documents;

namespace Webignite.Core.Books;

/// <summary>One account's total in a report: what it holds, the way a person reads it (an expense of R 100 is 100, not -100).</summary>
public sealed record AccountTotal(Account Account, decimal Amount);

/// <summary>Every account's debits and credits to a day. The two columns add up to the same when the books are right.</summary>
public sealed record TrialBalance(DateOnly AsOf, IReadOnlyList<(Account Account, decimal Debit, decimal Credit)> Rows)
{
    public decimal TotalDebit => Rows.Sum(r => r.Debit);
    public decimal TotalCredit => Rows.Sum(r => r.Credit);
    public bool Balances => Money.Cents(TotalDebit) == Money.Cents(TotalCredit);
}

/// <summary>Income less expenses over a period.</summary>
public sealed record ProfitAndLoss(DateOnly From, DateOnly To, IReadOnlyList<AccountTotal> Income, IReadOnlyList<AccountTotal> Expenses)
{
    public decimal TotalIncome => Income.Sum(i => i.Amount);
    public decimal TotalExpenses => Expenses.Sum(e => e.Amount);
    public decimal Profit => TotalIncome - TotalExpenses;
}

/// <summary>What the business owns, owes and is worth on a day. Assets always equal liabilities plus equity.</summary>
public sealed record BalanceSheet(DateOnly AsOf, IReadOnlyList<AccountTotal> Assets, IReadOnlyList<AccountTotal> Liabilities,
    IReadOnlyList<AccountTotal> Equity, decimal ProfitThisYear)
{
    public decimal TotalAssets => Assets.Sum(a => a.Amount);
    public decimal TotalLiabilities => Liabilities.Sum(l => l.Amount);
    public decimal TotalEquity => Equity.Sum(e => e.Amount) + ProfitThisYear;
    public bool Balances => Money.Cents(TotalAssets) == Money.Cents(TotalLiabilities + TotalEquity);
}

public static class BookReports
{
    /// <summary>Each account's balance the way it reads: positive is normal (money in the bank, income earned, an expense paid).</summary>
    public static decimal Balance(Account account, IEnumerable<JournalEntry> entries, DateOnly from, DateOnly to)
    {
        var (debit, credit) = Sums(account, entries, from, to);
        return Money.Cents(account.GrowsWithDebit ? debit - credit : credit - debit);
    }

    public static TrialBalance Trial(IReadOnlyList<Account> chart, IReadOnlyList<JournalEntry> entries, DateOnly asOf) =>
        new(asOf, [.. chart
            .Select(a => (Account: a, Sums: Sums(a, entries, DateOnly.MinValue, asOf)))
            .Where(r => r.Sums.Debit != 0 || r.Sums.Credit != 0)
            .Select(r =>
            {
                var net = r.Sums.Debit - r.Sums.Credit;
                return (r.Account, Debit: Money.Cents(net > 0 ? net : 0), Credit: Money.Cents(net < 0 ? -net : 0));
            })]);

    public static ProfitAndLoss Profit(IReadOnlyList<Account> chart, IReadOnlyList<JournalEntry> entries, DateOnly from, DateOnly to) =>
        new(from, to, Totals(chart, entries, AccountType.Income, from, to), Totals(chart, entries, AccountType.Expense, from, to));

    /// <summary>
    /// The balance sheet on a day. Income and expenses before the tax year began are in equity as earnings kept
    /// (with whatever retained earnings the accountant posted); this tax year's profit shows on its own line.
    /// </summary>
    public static BalanceSheet Balances(IReadOnlyList<Account> chart, IReadOnlyList<JournalEntry> entries, DateOnly asOf)
    {
        var yearStart = TaxYears.Start(asOf);
        var earlier = Profit(chart, entries, DateOnly.MinValue, yearStart.AddDays(-1)).Profit;
        var equity = Totals(chart, entries, AccountType.Equity, DateOnly.MinValue, asOf).ToList();
        if (earlier != 0)
        {
            var kept = ChartOfAccounts.WithRole(chart, AccountRole.RetainedEarnings)!;
            var at = equity.FindIndex(e => e.Account.Code == kept.Code);
            if (at >= 0)
                equity[at] = equity[at] with { Amount = equity[at].Amount + earlier };
            else
                equity.Add(new AccountTotal(kept, earlier));
        }
        return new(asOf, Totals(chart, entries, AccountType.Asset, DateOnly.MinValue, asOf), Totals(chart, entries, AccountType.Liability, DateOnly.MinValue, asOf),
            equity, Profit(chart, entries, yearStart, asOf).Profit);
    }

    private static List<AccountTotal> Totals(IReadOnlyList<Account> chart, IReadOnlyList<JournalEntry> entries, AccountType type, DateOnly from, DateOnly to) =>
        [.. chart.Where(a => a.Type == type).Select(a => new AccountTotal(a, Balance(a, entries, from, to))).Where(t => t.Amount != 0)];

    private static (decimal Debit, decimal Credit) Sums(Account account, IEnumerable<JournalEntry> entries, DateOnly from, DateOnly to)
    {
        decimal debit = 0, credit = 0;
        foreach (var entry in entries.Where(e => e.Date >= from && e.Date <= to))
            foreach (var line in entry.Lines.Where(l => l.Account == account.Code))
            {
                debit += line.Debit;
                credit += line.Credit;
            }
        return (debit, credit);
    }
}

/// <summary>South Africa's tax year: 1 March to the end of February.</summary>
public static class TaxYears
{
    /// <summary>The first day of the tax year a day falls in.</summary>
    public static DateOnly Start(DateOnly day) => new(day.Month >= 3 ? day.Year : day.Year - 1, 3, 1);

    /// <summary>The last day of the tax year a day falls in (28 or 29 February).</summary>
    public static DateOnly End(DateOnly day) => Start(day).AddYears(1).AddDays(-1);

    /// <summary>How SARS names it: the year it ends in, e.g. "2027 tax year" for March 2026 to February 2027.</summary>
    public static string Name(DateOnly day) => $"{End(day).Year} tax year";
}
