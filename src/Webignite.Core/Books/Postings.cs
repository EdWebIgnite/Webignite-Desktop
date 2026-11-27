using Webignite.Core.Documents;

namespace Webignite.Core.Books;

/// <summary>
/// The books for quotes and invoices made in Webignite, worked out from them so they can never disagree: an invoice
/// is owed by the client and earned (split across income accounts by what each line was for), a payment moves it
/// into the bank, a credit note takes it back off, and a void undoes the invoice on the day it was voided. Quotes
/// don't touch the books. Nothing here is typed or stored; change the invoice and the books follow.
/// </summary>
public static class Postings
{
    /// <summary>The income account a line goes to: hosting and domains, retainers and maintenance, else design and development.</summary>
    public static string IncomeFor(Line line)
    {
        var words = $"{line.Product} {line.Description}".ToLowerInvariant();
        if (words.Contains("hosting", StringComparison.Ordinal) || words.Contains("domain", StringComparison.Ordinal) || words.Contains("ssl", StringComparison.Ordinal))
            return "4100";
        if (words.Contains("retainer", StringComparison.Ordinal) || words.Contains("maintenance", StringComparison.Ordinal)
            || words.Contains("care plan", StringComparison.Ordinal) || words.Contains("monthly", StringComparison.Ordinal) || words.Contains("seo", StringComparison.Ordinal))
            return "4200";
        return "4000";
    }

    /// <summary>The journal entries one quote or invoice makes. Empty for quotes, drafts and ones in Xero.</summary>
    public static IReadOnlyList<JournalEntry> For(StoredDocument s, IReadOnlyCollection<Account> chart)
    {
        if (s.Status != DocumentStatus.Issued || s.Own is not { } own || s.Document.Kind != DocumentKind.Invoice)
            return [];
        var debtors = ChartOfAccounts.WithRole(chart, AccountRole.Debtors)!.Code;
        var bank = ChartOfAccounts.WithRole(chart, AccountRole.Bank)!.Code;
        var client = s.Document.Customer.Name;
        var number = own.Number;
        var date = s.Document.Date ?? DateOnly.FromDateTime(own.Issued.LocalDateTime);
        var income = Income(s.Document);
        var total = income.Sum(i => i.Amount);
        var entries = new List<JournalEntry>();
        if (total > 0)
        {
            entries.Add(new JournalEntry
            {
                Id = $"invoice-{s.Id}", Date = date, Memo = $"Invoice {number}, {client}", Source = JournalSource.Invoice, SourceId = s.Id,
                Lines = [JournalLine.Dr(debtors, total, client), .. income.Select(i => JournalLine.Cr(i.Account, i.Amount, client))],
            });
        }
        foreach (var (payment, at) in own.Payments.Select((p, i) => (p, i)))
            entries.Add(new JournalEntry
            {
                Id = $"payment-{s.Id}-{at}", Date = payment.Date, Memo = $"Payment for {number}, {client}" + (payment.Note is { } note ? $" ({note})" : ""),
                Source = JournalSource.Payment, SourceId = s.Id,
                Lines = [JournalLine.Dr(bank, payment.Amount, client), JournalLine.Cr(debtors, payment.Amount, client)],
            });
        foreach (var credit in own.Credits)
        {
            // A credit note takes its share off each income account, in proportion to the invoice.
            var shares = Share(income, total, credit.Amount);
            entries.Add(new JournalEntry
            {
                Id = $"credit-{s.Id}-{credit.Number}", Date = credit.Date, Memo = $"Credit note {credit.Number} on {number}, {client}",
                Source = JournalSource.CreditNote, SourceId = s.Id,
                Lines = [.. shares.Select(x => JournalLine.Dr(x.Account, x.Amount, client)), JournalLine.Cr(debtors, credit.Amount, client)],
            });
        }
        if (own.Voided is { } voided && total > 0)
            entries.Add(new JournalEntry
            {
                Id = $"void-{s.Id}", Date = DateOnly.FromDateTime(voided.LocalDateTime), Memo = $"Invoice {number} voided, {client}", Source = JournalSource.Invoice, SourceId = s.Id,
                Lines = [.. income.Select(i => JournalLine.Dr(i.Account, i.Amount, client)), JournalLine.Cr(debtors, total, client)],
            });
        return entries;
    }

    /// <summary>Every entry the quotes and invoices make, oldest first.</summary>
    public static IReadOnlyList<JournalEntry> All(IEnumerable<StoredDocument> documents, IReadOnlyCollection<Account> chart) =>
        [.. documents.SelectMany(d => For(d, chart)).OrderBy(e => e.Date).ThenBy(e => e.Id, StringComparer.Ordinal)];

    /// <summary>
    /// The whole books: what the invoices made here make, and the money typed in or added on the money page, leaving
    /// out money entries that are payments of those invoices (the invoice already has them).
    /// </summary>
    public static IReadOnlyList<JournalEntry> Books(IEnumerable<StoredDocument> documents, IEnumerable<Webignite.Core.Finance.MoneyEntry> money, IReadOnlyCollection<Account> chart) =>
        Books(documents, money, [], chart);

    /// <summary>The same, with the supplier's bills kept in Webignite: each bill and its payment, and not the expense paying it again.</summary>
    public static IReadOnlyList<JournalEntry> Books(IEnumerable<StoredDocument> documents, IEnumerable<Webignite.Core.Finance.MoneyEntry> money,
        IEnumerable<Webignite.Core.Finance.SupplierBill> bills, IReadOnlyCollection<Account> chart) =>
        [.. All(documents, chart)
            .Concat(money.Where(m => m.DocumentId is null && m.BillId is null).Select(m => Journals.FromMoneyEntry(m, chart)))
            .Concat(bills.SelectMany(b => Webignite.Core.Finance.SupplierBills.Postings(b, chart)))
            .OrderBy(e => e.Date).ThenBy(e => e.Id, StringComparer.Ordinal)];

    // The invoice's income by account, after its discount, summing to its total.
    private static List<(string Account, decimal Amount)> Income(Document doc)
    {
        var lines = doc.Lines.Where(l => l.IsPriced).GroupBy(IncomeFor).Select(g => (Account: g.Key, Amount: g.Sum(l => l.Amount()))).ToList();
        var gross = lines.Sum(l => l.Amount);
        var net = doc.Total ?? doc.NetSubtotal();
        if (gross == 0 || lines.Count == 0)
            return net > 0 ? [("4000", Money.Cents(net))] : [];
        return Share(lines, gross, net);
    }

    // An amount split across accounts in proportion, the cents left over going to the largest so it adds up exactly.
    private static List<(string Account, decimal Amount)> Share(List<(string Account, decimal Amount)> parts, decimal whole, decimal amount)
    {
        if (whole == 0)
            return [];
        var shares = parts.Select(p => (p.Account, Amount: Money.Cents(amount * p.Amount / whole))).ToList();
        var left = Money.Cents(amount - shares.Sum(x => x.Amount));
        if (left != 0)
        {
            var biggest = shares.FindIndex(x => x.Amount == shares.Max(y => y.Amount));
            shares[biggest] = (shares[biggest].Account, shares[biggest].Amount + left);
        }
        return [.. shares.Where(x => x.Amount != 0)];
    }
}
