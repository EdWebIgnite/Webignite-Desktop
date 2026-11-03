using Webignite.Core.Clients;
using Webignite.Core.Layout;
using Webignite.Core.Tasks;

namespace Webignite.Tests;

/// <summary>The one line under a short list: a hint or the next useful thing to do.</summary>
public sealed class ListHintTests
{
    private static readonly DateOnly Today = Samples.Today;

    private static StoredDocument Doc(DocumentKind kind, decimal amount, DateOnly due, DocumentStatus status = DocumentStatus.InXero,
        string? settledAs = null, string? xeroStatus = "AUTHORISED") => new()
    {
        Id = StoredDocument.NewId(),
        Status = status,
        XeroStatus = status == DocumentStatus.InXero ? xeroStatus : null,
        SettledAs = settledAs,
        Settled = settledAs is null ? null : DateTimeOffset.UtcNow,
        Document = Samples.Completed(Samples.NewInvoice(Samples.Priced("Website", 1, amount))) with
        {
            Kind = kind,
            DueOrExpiryDate = due,
            Customer = new Customer { Name = "Karoo Coffee Roasters" },
        },
    };

    private static StoredDocument Invoice(decimal amount, DateOnly due, DocumentStatus status = DocumentStatus.InXero,
        string? settledAs = null, string? xeroStatus = "AUTHORISED") => Doc(DocumentKind.Invoice, amount, due, status, settledAs, xeroStatus);

    private static StoredDocument Quote(DateOnly expires, DocumentStatus status = DocumentStatus.InXero,
        string? settledAs = null, string? xeroStatus = "SENT") => Doc(DocumentKind.Quote, 1000, expires, status, settledAs, xeroStatus);

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(5, false, true)]
    [InlineData(6, false, false)]
    [InlineData(3, true, false)]
    public void Only_short_unfiltered_lists_get_a_hint(int rows, bool filtered, bool expected) =>
        Assert.Equal(expected, ListHints.Shows(rows, filtered));

    [Fact]
    public void No_documents_means_no_hint_because_the_empty_state_says_it() =>
        Assert.Null(DocumentHeadline.Hint(DocumentKind.Invoice, [], Today));

    [Fact]
    public void Overdue_invoices_come_first_with_what_they_add_up_to()
    {
        var hint = DocumentHeadline.Hint(DocumentKind.Invoice,
            [Invoice(1500, Today.AddDays(-3)), Invoice(900, Today.AddDays(-1)), Invoice(400, Today.AddDays(5), DocumentStatus.Draft)], Today);
        Assert.NotNull(hint);
        Assert.Equal($"2 invoices are overdue, {Money.Rand(2400m)} in all.", hint.Text);
        Assert.Equal(DocumentShow.Overdue, hint.Show);
        Assert.Equal("Show overdue", hint.ActionText);
    }

    [Fact]
    public void One_overdue_invoice_reads_naturally()
    {
        var hint = DocumentHeadline.Hint(DocumentKind.Invoice, [Invoice(1500, Today.AddDays(-3))], Today);
        Assert.Equal($"1 invoice is overdue, {Money.Rand(1500m)}.", hint?.Text);
    }

    [Fact]
    public void Then_what_is_still_on_this_pc()
    {
        var hint = DocumentHeadline.Hint(DocumentKind.Invoice,
            [Invoice(1500, Today.AddDays(10)), Invoice(400, Today.AddDays(5), DocumentStatus.Draft)], Today);
        Assert.NotNull(hint);
        Assert.Equal("1 invoice is still on this PC. Open it and send it to Xero as a draft when it's ready.", hint.Text);
        Assert.Equal(DocumentShow.NotInXero, hint.Show);
        Assert.Equal("Show it", hint.ActionText);
    }

    [Fact]
    public void Then_drafts_waiting_for_approval_in_xero_with_nothing_to_click()
    {
        var hint = DocumentHeadline.Hint(DocumentKind.Invoice,
            [Invoice(1500, Today.AddDays(10), xeroStatus: "DRAFT"), Invoice(800, Today.AddDays(10), xeroStatus: "DRAFT")], Today);
        Assert.NotNull(hint);
        Assert.Equal("2 invoices are drafts in Xero. Approve them in Xero when they can go to the client.", hint.Text);
        Assert.Null(hint.ActionText);
    }

    [Fact]
    public void Accepted_quotes_with_no_invoice_point_to_make_invoice()
    {
        var hint = DocumentHeadline.Hint(DocumentKind.Quote,
            [Quote(Today.AddDays(10), settledAs: "accepted"), Quote(Today.AddDays(10), settledAs: "invoiced")], Today);
        Assert.NotNull(hint);
        Assert.Equal("1 accepted quote has no invoice yet. Open it and pick Make invoice.", hint.Text);
        Assert.Equal(DocumentShow.Settled, hint.Show);
    }

    [Fact]
    public void With_nothing_to_sort_out_it_offers_a_new_one()
    {
        var invoices = DocumentHeadline.Hint(DocumentKind.Invoice, [Invoice(1500, Today.AddDays(10), settledAs: "paid")], Today);
        Assert.Equal("New invoice", invoices?.ActionText);
        Assert.Null(invoices?.Show);

        var quotes = DocumentHeadline.Hint(DocumentKind.Quote, [Quote(Today.AddDays(10))], Today);
        Assert.Equal("New quote", quotes?.ActionText);
        Assert.Contains("To-do", quotes?.Text);
    }

    [Fact]
    public void Hints_only_look_at_their_own_kind()
    {
        var hint = DocumentHeadline.Hint(DocumentKind.Quote, [Quote(Today.AddDays(10)), Invoice(1500, Today.AddDays(-3))], Today);
        Assert.Equal("New quote", hint?.ActionText);
    }

    private static ClientSummary Summary(string name, string? email = null) =>
        new(new Client { Id = Client.NewId(), Name = name, Email = email }, [], [], []);

    [Fact]
    public void Clients_with_no_email_come_first_in_name_order()
    {
        var hint = ClientDirectory.Hint([Summary("Karoo Coffee"), Summary("Bosveld Builders"), Summary("Fynbos Physio", "info@fynbos.co.za")]);
        Assert.NotNull(hint);
        Assert.Equal("2 clients have no email address saved yet.", hint.Text);
        Assert.Equal("Start with Bosveld Builders", hint.ActionText);
        Assert.Equal("Bosveld Builders", hint.Open?.Name);
    }

    [Fact]
    public void One_client_with_no_email_is_named()
    {
        var hint = ClientDirectory.Hint([Summary("Karoo Coffee"), Summary("Fynbos Physio", "info@fynbos.co.za")]);
        Assert.Equal("Karoo Coffee has no email address saved yet.", hint?.Text);
        Assert.Equal("Add it", hint?.ActionText);
    }

    [Fact]
    public void Clients_all_with_email_get_the_new_client_hint()
    {
        var hint = ClientDirectory.Hint([Summary("Fynbos Physio", "info@fynbos.co.za")]);
        Assert.Equal("New client", hint?.ActionText);
        Assert.Null(hint?.Open);
        Assert.Null(ClientDirectory.Hint([]));
    }

    private static TaskItem Task(string title, DateOnly? due = null, bool done = false) =>
        new() { Id = TaskItem.NewId(), Title = title, Due = due, Done = done, Created = DateTimeOffset.UtcNow };

    [Fact]
    public void A_quiet_day_names_the_next_to_do()
    {
        // Samples.Today is Saturday 31 October 2026.
        Assert.Equal("Nothing due today. Next up: Call Karoo, tomorrow.",
            TaskGroups.Hint([Task("Call Karoo", Today.AddDays(1)), Task("Send logo", Today.AddDays(4))], Today));
        Assert.Equal("Nothing due today. Next up: Send logo, Wednesday.",
            TaskGroups.Hint([Task("Send logo", Today.AddDays(4)), Task("Old one", Today.AddDays(-2), done: true)], Today));
        Assert.Equal("Nothing due today. Next up: Renew domain, 20 Nov.",
            TaskGroups.Hint([Task("Renew domain", Today.AddDays(20))], Today));
    }

    [Fact]
    public void A_busy_day_or_no_dates_explains_what_arrives_by_itself()
    {
        var expected = "Overdue invoices, quotes waiting on a client and forgotten drafts land here by themselves.";
        Assert.Equal(expected, TaskGroups.Hint([Task("Call Karoo", Today), Task("Send logo", Today.AddDays(4))], Today));
        Assert.Equal(expected, TaskGroups.Hint([Task("Someday")], Today));
        Assert.Equal(expected, TaskGroups.Hint([Task("Done", Today, done: true)], Today));
        Assert.Null(TaskGroups.Hint([], Today));
    }
}
