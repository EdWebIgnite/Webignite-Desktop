using Webignite.Core.Documents;

namespace Webignite.Tests;

/// <summary>Quotes and invoices finished, sent and paid in Webignite without Xero, and their numbers.</summary>
public class OwnDocumentTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);

    private static StoredDocument Draft(DocumentKind kind = DocumentKind.Invoice, decimal total = 1000, DateOnly? due = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Document = new Document
        {
            Kind = kind,
            Origin = DocumentOrigin.New,
            Customer = new Customer { Name = "G3" },
            Date = Today,
            DueOrExpiryDate = due ?? Today.AddDays(7),
            Lines = [new Line { Description = "Work", Quantity = 1, UnitPrice = total }],
            Total = total,
        },
    };

    private static StoredDocument InXero(string number, DocumentKind kind = DocumentKind.Invoice) =>
        Draft(kind) with { Status = DocumentStatus.InXero, Xero = new XeroLink { Id = number, Number = number } };

    [Fact]
    public void The_next_number_carries_on_from_the_highest_used_in_Xero()
    {
        var documents = new[] { InXero("INV-0110"), InXero("INV-0112"), InXero("QU-1024", DocumentKind.Quote) };

        Assert.Equal("INV-0113", DocumentNumbers.Next(DocumentKind.Invoice, documents, null));
        Assert.Equal("QU-1025", DocumentNumbers.Next(DocumentKind.Quote, documents, null));
    }

    [Fact]
    public void A_number_set_in_Settings_is_used_unless_it_is_already_behind()
    {
        var documents = new[] { InXero("INV-0112") };

        Assert.Equal("INV-0200", DocumentNumbers.Next(DocumentKind.Invoice, documents, "INV-0200"));
        Assert.Equal("INV-0113", DocumentNumbers.Next(DocumentKind.Invoice, documents, "INV-0100"));
        Assert.Equal("WI-0001", DocumentNumbers.Next(DocumentKind.Invoice, documents, "WI-0001"));
    }

    [Fact]
    public void With_nothing_used_numbering_starts_at_one()
    {
        Assert.Equal("INV-0001", DocumentNumbers.Next(DocumentKind.Invoice, [], null));
        Assert.Equal("QU-0001", DocumentNumbers.Next(DocumentKind.Quote, [], null));
    }

    [Fact]
    public void Numbers_finished_here_count_as_used_and_a_used_number_is_refused()
    {
        var finished = OwnDocuments.Issue(Draft(), "INV-0113", Now);
        var documents = new[] { InXero("INV-0112"), finished };

        Assert.Equal("INV-0114", DocumentNumbers.Next(DocumentKind.Invoice, documents, null));
        Assert.Equal("INV-0113 is already used for G3.", DocumentNumbers.Problem("inv-0113", Draft(), DocumentKind.Invoice, documents));
        Assert.Null(DocumentNumbers.Problem("INV-0113", finished, DocumentKind.Invoice, documents));
        Assert.Null(DocumentNumbers.Problem("INV-0113", Draft(DocumentKind.Quote), DocumentKind.Quote, documents));
        Assert.Equal("Give it a number.", DocumentNumbers.Problem(" ", Draft(), DocumentKind.Invoice, documents));
    }

    [Fact]
    public void A_finished_invoice_counts_as_owed_until_it_is_paid_in_full()
    {
        var invoice = OwnDocuments.Issue(Draft(total: 1000), "INV-0113", Now);

        Assert.Equal("INV-0113", invoice.Number);
        Assert.Equal(1000, Receivables.Outstanding(invoice));
        Assert.Equal("Not sent yet", Receivables.StatusText(invoice, Today));

        var part = OwnDocuments.Pay(invoice, 400, Today, Now);
        Assert.Equal(600, Receivables.Outstanding(part));
        Assert.Equal("Part paid", Receivables.StatusText(part, Today));
        Assert.Null(part.Settled);

        var paid = OwnDocuments.Pay(part, 600, Today, Now);
        Assert.Equal(Settlement.Paid, paid.Settled?.As);
        Assert.Equal(0, Receivables.Outstanding(paid));
        Assert.Equal(Today, Receivables.PaidOn(paid));
        Assert.Equal("Paid", Receivables.StatusText(paid, Today));

        var undone = OwnDocuments.Unpay(paid);
        Assert.Null(undone.Settled);
        Assert.Equal(600, Receivables.Outstanding(undone));
    }

    [Fact]
    public void Payments_are_checked_before_they_are_recorded()
    {
        var invoice = OwnDocuments.Issue(Draft(total: 1000), "INV-0113", Now);

        Assert.Equal($"That's more than the {Money.Rand(1000)} still owed.", OwnDocuments.PaymentProblem(invoice, 1500, Today, Today));
        Assert.Equal("The day it was paid can't be in the future.", OwnDocuments.PaymentProblem(invoice, 100, Today.AddDays(1), Today));
        Assert.Null(OwnDocuments.PaymentProblem(invoice, 1000, Today, Today));
    }

    [Fact]
    public void A_finished_invoice_past_its_due_date_is_overdue()
    {
        var invoice = OwnDocuments.Issue(Draft(due: Today.AddDays(-1)), "INV-0113", Now);

        Assert.True(Receivables.IsOverdue(invoice, Today));
        Assert.Equal("Overdue", Receivables.StatusText(invoice, Today));
    }

    [Fact]
    public void A_finished_quote_waits_on_the_client_once_sent_and_can_then_be_answered()
    {
        var quote = OwnDocuments.Issue(Draft(DocumentKind.Quote), "QU-1025", Now);
        Assert.False(Receivables.IsOpenQuote(quote, Today));
        Assert.False(OwnDocuments.CanDecide(quote));

        var sent = OwnDocuments.MarkSent(quote, Now);
        Assert.True(Receivables.IsOpenQuote(sent, Today));
        Assert.Equal("Sent", Receivables.StatusText(sent, Today));

        var accepted = OwnDocuments.Decide(sent, accepted: true, Now);
        Assert.Equal(Settlement.Accepted, accepted.Settled?.As);
        Assert.False(Receivables.IsOpenQuote(accepted, Today));
    }

    [Fact]
    public void Change_it_works_only_while_the_client_has_not_had_it()
    {
        var invoice = OwnDocuments.Issue(Draft(), "INV-0113", Now);
        Assert.True(OwnDocuments.CanReopen(invoice));

        var reopened = OwnDocuments.Reopen(invoice);
        Assert.Equal(DocumentStatus.Draft, reopened.Status);
        Assert.Equal("INV-0113", reopened.Document.Number);
        Assert.Null(reopened.Number);

        Assert.False(OwnDocuments.CanReopen(OwnDocuments.Emailed(invoice, "Francois", Now)));
        Assert.False(OwnDocuments.CanReopen(OwnDocuments.Pay(invoice, 100, Today, Now)));
    }

    [Fact]
    public void A_cancelled_invoice_is_no_longer_owed_and_cannot_be_deleted()
    {
        var invoice = OwnDocuments.Issue(Draft(), "INV-0113", Now);
        Assert.False(Approvals.CanDelete(invoice));
        Assert.True(OwnDocuments.CanCancel(invoice));

        var cancelled = OwnDocuments.Cancel(invoice, Now);
        Assert.Equal(0, Receivables.Outstanding(cancelled));
        Assert.Equal("Cancelled", Receivables.StatusText(cancelled, Today));
        Assert.False(OwnDocuments.CanCancel(OwnDocuments.Pay(invoice, 100, Today, Now)));
    }

    [Fact]
    public void Only_drafts_not_in_Xero_can_be_finished_here()
    {
        Assert.True(OwnDocuments.CanIssue(Draft()));
        Assert.False(OwnDocuments.CanIssue(InXero("INV-0112")));
        Assert.False(OwnDocuments.CanIssue(OwnDocuments.Issue(Draft(), "INV-0113", Now)));
    }
}

public class OwnDocumentAssistantTests
{
    [Fact]
    public void Claude_and_ChatGPT_cannot_change_a_quote_or_invoice_once_it_is_finished_here()
    {
        var draft = new StoredDocument
        {
            Id = "d",
            Document = new Document { Kind = DocumentKind.Invoice, Origin = DocumentOrigin.New, Customer = new Customer { Name = "G3" }, Lines = [] },
        };

        Assert.True(Approvals.AssistantMayChange(draft));
        Assert.False(Approvals.AssistantMayChange(OwnDocuments.Issue(draft, "INV-0113", DateTimeOffset.UnixEpoch)));
    }
}
