using Webignite.Core.Documents;

namespace Webignite.Core.Books;

/// <summary>One side of a journal entry: an amount to one account, as a debit or a credit (one of the two is zero).</summary>
public sealed record JournalLine(string Account, decimal Debit, decimal Credit, string? Party = null)
{
    public static JournalLine Dr(string account, decimal amount, string? party = null) => new(account, Money.Cents(amount), 0, party);
    public static JournalLine Cr(string account, decimal amount, string? party = null) => new(account, 0, Money.Cents(amount), party);
}

/// <summary>Where a journal entry came from, so the books can be traced back to what Eddie did.</summary>
public enum JournalSource { Manual, Invoice, Payment, CreditNote, Bill, BillPayment, MoneyIn, MoneyOut, Opening }

/// <summary>
/// One event in the books: what happened on a day, as debits and credits that add up to the same amount. The app
/// makes these from what Eddie does (an invoice, a payment, an expense); the accountant can add her own.
/// </summary>
public sealed record JournalEntry
{
    public required string Id { get; init; }
    public required DateOnly Date { get; init; }
    public required string Memo { get; init; }
    public required IReadOnlyList<JournalLine> Lines { get; init; }
    public JournalSource Source { get; init; } = JournalSource.Manual;

    /// <summary>The id of what it came from (the invoice, payment or money entry), null for one typed by the accountant.</summary>
    public string? SourceId { get; init; }

    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
}

public static class Journals
{
    /// <summary>Why an entry can't go into the books, or null when it can: it must balance, and use accounts that exist.</summary>
    public static string? Problem(JournalEntry entry, IReadOnlyCollection<Account> chart)
    {
        if (entry.Lines.Count < 2)
            return "A journal needs at least two lines.";
        if (entry.Lines.Any(l => l.Debit < 0 || l.Credit < 0 || (l.Debit != 0 && l.Credit != 0) || (l.Debit == 0 && l.Credit == 0)))
            return "Each line is either a debit or a credit, more than zero.";
        if (Money.Cents(entry.TotalDebit) != Money.Cents(entry.TotalCredit))
            return $"Debits ({Money.Rand(entry.TotalDebit)}) and credits ({Money.Rand(entry.TotalCredit)}) must be the same.";
        if (entry.Lines.FirstOrDefault(l => chart.All(a => a.Code != l.Account)) is { } unknown)
            return $"There's no account {unknown.Account}.";
        return null;
    }

    /// <summary>
    /// The journal for an amount typed in on the money page. Money in that isn't an invoice payment is other income;
    /// money out goes to the expense account for its category. Both through the bank.
    /// </summary>
    public static JournalEntry FromMoneyEntry(Webignite.Core.Finance.MoneyEntry entry, IReadOnlyCollection<Account> chart)
    {
        var bank = ChartOfAccounts.WithRole(chart, AccountRole.Bank)!.Code;
        return entry.Kind == Webignite.Core.Finance.EntryKind.Received
            ? new JournalEntry
            {
                Id = "money-" + entry.Id, Date = entry.Date, Memo = Memo("Money in", entry), Source = JournalSource.MoneyIn, SourceId = entry.Id,
                Lines = [JournalLine.Dr(bank, entry.Amount, entry.Party), JournalLine.Cr("4900", entry.Amount, entry.Party)],
            }
            : new JournalEntry
            {
                Id = "money-" + entry.Id, Date = entry.Date, Memo = Memo("Money out", entry), Source = JournalSource.MoneyOut, SourceId = entry.Id,
                Lines = [JournalLine.Dr(ChartOfAccounts.ForExpenseCategory(entry.Category), entry.Amount, entry.Party), JournalLine.Cr(bank, entry.Amount, entry.Party)],
            };
    }

    private static string Memo(string what, Webignite.Core.Finance.MoneyEntry entry) =>
        string.Join(": ", new[] { entry.Party, entry.Note }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } said ? $"{what}, {said}" : what;
}
