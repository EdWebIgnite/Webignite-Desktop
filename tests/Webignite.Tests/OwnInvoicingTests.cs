namespace Webignite.Tests;

/// <summary>Quotes and invoices made final in Webignite instead of Xero: numbering, issuing, payments, and how they count.</summary>
public sealed class OwnInvoicingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.FromHours(2));

    private static StoredDocument Draft(decimal total = 1000m, DocumentKind kind = DocumentKind.Invoice) =>
        Samples.Stored(Samples.Completed(Samples.NewInvoice(Samples.Priced("Website", 1, total)) with { Kind = kind }));

    [Fact]
    public void Numbers_carry_on_from_the_highest_one_with_its_prefix_and_zeros()
    {
        StoredDocument[] all = [Samples.InXero("a").WithXero(x => x with { Number = "INV-0105" }), Samples.InXero("b").WithXero(x => x with { Number = "INV-0107" }),
            Samples.InXero("c").WithXero(x => x with { Number = "TEST-9" })];
        Assert.Equal("INV-0108", OwnInvoicing.NextNumber(DocumentKind.Invoice, all));
        Assert.Equal("QU-0001", OwnInvoicing.NextNumber(DocumentKind.Quote, all));
        Assert.Equal("INV-0001", OwnInvoicing.NextNumber(DocumentKind.Invoice, []));
    }

    [Fact]
    public void Issuing_gives_the_next_number_and_counts_it_as_owed()
    {
        var earlier = Samples.InXero("x").WithXero(x => x with { Number = "INV-0107" });
        var issued = OwnInvoicing.Issue(Draft(), [earlier], Now)!;

        Assert.Equal((DocumentStatus.Issued, "INV-0108", "INV-0108"), (issued.Status, issued.Own!.Number, OwnInvoicing.NumberOf(issued)));
        Assert.True(Receivables.IsOpenInvoice(issued));
        Assert.Equal(1000m, Receivables.Outstanding(issued));
        Assert.Equal("Sent", Receivables.StatusText(issued, Samples.Today));
        Assert.Null(OwnInvoicing.Issue(issued, [earlier], Now));
    }

    [Fact]
    public void A_number_typed_on_the_draft_is_kept_unless_another_has_it()
    {
        var typed = Draft() with { Document = Draft().Document with { Number = "WEB-2026-014" } };
        Assert.Equal("WEB-2026-014", OwnInvoicing.Issue(typed, [], Now)!.Own!.Number);
        var taken = Samples.InXero("x").WithXero(x => x with { Number = "WEB-2026-014" });
        Assert.NotEqual("WEB-2026-014", OwnInvoicing.Issue(typed, [taken], Now)!.Own!.Number);
    }

    [Fact]
    public void Payments_pay_it_off_and_a_last_one_settles_it_as_paid()
    {
        var issued = OwnInvoicing.Issue(Draft(1000m), [], Now)!;
        var part = OwnInvoicing.Record(issued, 400m, Samples.Today, "EFT", Now)!;
        Assert.Equal((600m, "Part paid"), (OwnInvoicing.Due(part), Receivables.StatusText(part, Samples.Today)));
        Assert.Equal("That's more than the R 600.00 still owed.", OwnInvoicing.Check(part, 700m, Samples.Today, Samples.Today)!.Replace(' ', ' '));
        Assert.Null(OwnInvoicing.Record(part, 700m, Samples.Today, null, Now));

        var paid = OwnInvoicing.Record(part, 600m, Samples.Today, null, Now)!;
        Assert.Equal(Settlement.Paid, paid.Settled?.As);
        Assert.Equal(Samples.Today, paid.Xero.PaidOn);
        Assert.False(Receivables.IsOpenInvoice(paid));
        Assert.False(OwnInvoicing.CanRecord(paid));
    }

    [Fact]
    public void An_issued_quote_waits_on_the_client_until_it_expires()
    {
        var quote = OwnInvoicing.Issue(Draft(kind: DocumentKind.Quote), [], Now)!;
        Assert.StartsWith("QU-", quote.Own!.Number, StringComparison.Ordinal);
        Assert.True(Receivables.IsOpenQuote(quote, Samples.Today));
        Assert.False(Receivables.IsOpenQuote(quote, quote.Document.DueOrExpiryDate!.Value.AddDays(1)));
    }

    [Fact]
    public void Payments_recorded_on_both_PCs_are_all_kept_when_they_meet()
    {
        var issued = OwnInvoicing.Issue(Draft(1000m), [], Now)!;
        var here = OwnInvoicing.Record(issued, 300m, Samples.Today, "EFT", Now)!;
        var there = OwnInvoicing.Record(issued, 200m, Samples.Today, "Cash", Now.AddMinutes(1))! with { Updated = Now.AddMinutes(2) };
        var merged = Webignite.Core.Sync.SharedMerge.Document(here, there);
        Assert.Equal(500m, OwnInvoicing.Paid(merged));
        Assert.Equal(DocumentStatus.Issued, merged.Status);
    }
}
