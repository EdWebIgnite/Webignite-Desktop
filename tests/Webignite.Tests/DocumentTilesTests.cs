namespace Webignite.Tests;

/// <summary>The three figures at the top of the quote and invoice lists, and the rows each one shows.</summary>
public sealed class DocumentTilesTests
{
    private static readonly DateOnly Today = Samples.Today;

    private static StoredDocument Invoice(decimal amount, DateOnly due, Settlement? settled = null, DateOnly? paidOn = null) => new()
    {
        Id = StoredDocument.NewId(),
        Status = DocumentStatus.InXero,
        Settled = settled is null ? null : new SettledState { On = DateTimeOffset.UtcNow, As = settled },
        Xero = new XeroLink { Status = "AUTHORISED", PaidOn = paidOn },
        Document = Samples.Completed(Samples.NewInvoice(Samples.Priced("Website", 1, amount))) with { DueOrExpiryDate = due },
    };

    [Fact]
    public void Invoices_show_what_is_owed_overdue_and_paid_this_month()
    {
        StoredDocument[] all = [
            Invoice(2800m, Today.AddDays(-9)),
            Invoice(1000m, Today.AddDays(5)),
            Invoice(7250m, Today.AddDays(-1), Settlement.Paid, Today),
        ];
        var tiles = DocumentTiles.For(DocumentKind.Invoice, all, Today);

        Assert.Equal((Money.Rand(3800m), "2 invoices", DocumentShow.Owing), (tiles[0].Amount, tiles[0].Count, tiles[0].Show));
        Assert.Equal((Money.Rand(2800m), "1 invoice", true), (tiles[1].Amount, tiles[1].Count, tiles[1].Problem));
        Assert.Equal((Money.Rand(7250m), DocumentShow.Settled), (tiles[2].Amount, tiles[2].Show));

        // The Owed figure's rows are the ones it adds up.
        Assert.Equal(2, new DocumentFilter(Show: DocumentShow.Owing).Apply(all, Today).Count);
    }

    [Fact]
    public void Nothing_overdue_is_not_shown_as_a_problem()
    {
        var tiles = DocumentTiles.For(DocumentKind.Invoice, [Invoice(1000m, Today.AddDays(5))], Today);
        Assert.False(tiles[1].Problem);
        Assert.Equal("0 invoices", tiles[1].Count);
    }
}
