using Webignite.Core.Books;

namespace Webignite.Tests;

/// <summary>Putting right invoices made in Webignite (void, credit notes), the client's answer to a quote, and the books they make.</summary>
public sealed class OwnInvoicingStep2Tests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.FromHours(2));
    private static readonly IReadOnlyList<Account> Chart = ChartOfAccounts.Default;

    private static StoredDocument Issued(params Line[] lines) =>
        OwnInvoicing.Issue(Samples.Stored(Samples.Completed(Samples.NewInvoice(lines.Length > 0 ? lines : [Samples.Priced("Website", 1, 1000m)]))), [], Now)!;

    [Fact]
    public void Void_only_while_nothing_is_paid_and_then_nothing_is_owed()
    {
        var invoice = Issued();
        var voided = OwnInvoicing.Void(invoice, Now)!;
        Assert.Equal(Settlement.Voided, voided.Settled?.As);
        Assert.Equal(0m, Receivables.Outstanding(voided));
        Assert.False(OwnInvoicing.CanVoid(OwnInvoicing.Record(invoice, 100m, Samples.Today, null, Now)!));
    }

    [Fact]
    public void Credit_notes_get_their_own_numbers_and_take_their_amount_off()
    {
        var invoice = Issued();
        var part = OwnInvoicing.Credit(invoice, 300m, "Hosting cancelled", Samples.Today, [], Now)!;
        Assert.Equal(("CN-0001", 700m, "Part credited"), (part.Own!.Credits[0].Number, OwnInvoicing.Due(part), OwnInvoicing.Status(part)));

        var all = OwnInvoicing.Credit(part, null, null, Samples.Today, [part], Now)!;
        Assert.Equal(("CN-0002", Settlement.Credited), (all.Own!.Credits[1].Number, all.Settled?.As));
        Assert.True(Settlements.IsCancelled(all.Settled?.As));
        Assert.NotNull(OwnInvoicing.CheckCredit(part, 900m));
    }

    [Fact]
    public void An_issued_quote_takes_the_clients_answer()
    {
        var quote = OwnInvoicing.Issue(Samples.Stored(Samples.Completed(Samples.NewInvoice(Samples.Priced("Website", 1, 5000m)) with { Kind = DocumentKind.Quote })), [], Now)!;
        Assert.True(QuoteOutcomes.CanDecide(quote));
        var accepted = QuoteOutcomes.Decided(quote, accepted: true, Now);
        Assert.Equal(Settlement.Accepted, accepted.Settled?.As);
        Assert.False(QuoteOutcomes.CanDecide(accepted));
        Assert.Empty(Postings.For(accepted, Chart));
    }

    [Fact]
    public void Lines_go_to_income_by_what_they_were_for()
    {
        Assert.Equal("4100", Postings.IncomeFor(Samples.Priced("Website hosting, November", 1, 450m)));
        Assert.Equal("4200", Postings.IncomeFor(Samples.Priced("Monthly SEO", 1, 2800m)));
        Assert.Equal("4000", Postings.IncomeFor(Samples.Priced("Six page website", 1, 14500m)));
    }

    [Fact]
    public void The_books_balance_and_what_clients_owe_matches_the_invoices()
    {
        var invoice = Issued(Samples.Priced("Six page website", 1, 9000m), Samples.Priced("Website hosting, a year", 1, 1000m));
        invoice = OwnInvoicing.Record(invoice, 4000m, Samples.Today, "EFT", Now)!;
        invoice = OwnInvoicing.Credit(invoice, 1000m, "Discount agreed", Samples.Today, [invoice], Now)!;
        var voided = OwnInvoicing.Void(Issued(), Now)!;

        var entries = Postings.All([invoice, voided], Chart);
        Assert.All(entries, e => Assert.Null(Journals.Problem(e, Chart)));

        var asOf = Samples.Today.AddDays(30);
        Assert.True(BookReports.Trial(Chart, entries, asOf).Balances);
        decimal Held(string code) => BookReports.Balance(Chart.Single(a => a.Code == code), entries, DateOnly.MinValue, asOf);
        Assert.Equal(Receivables.Owed([invoice, voided]), Held("1100"));
        Assert.Equal(4000m, Held("1000"));
        // The credit note came off both income accounts in proportion: 9 000 less 900, 1 000 less 100.
        Assert.Equal(8100m, Held("4000"));
        Assert.Equal(900m, Held("4100"));
    }

    [Fact]
    public void A_payment_recorded_on_an_invoice_isnt_counted_twice_with_its_money_entry()
    {
        var invoice = OwnInvoicing.Record(Issued(), 1000m, Samples.Today, null, Now)!;
        var entry = new Webignite.Core.Finance.MoneyEntry { Id = "m", Kind = Webignite.Core.Finance.EntryKind.Received, Date = Samples.Today, Amount = 1000m, DocumentId = invoice.Id };
        var books = Postings.Books([invoice], [entry], Chart);
        Assert.Equal(1000m, BookReports.Balance(Chart.Single(a => a.Role == AccountRole.Bank), books, DateOnly.MinValue, Samples.Today));
    }
}
