using Webignite.Core.Clients;
using Webignite.Core.Overview;

namespace Webignite.Tests;

public sealed class StatementsTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static StoredDocument Invoice(string id, string customer, decimal total, DateTimeOffset? settled = null) => new()
    {
        Id = id,
        XeroId = "x" + id,
        XeroNumber = "INV-" + id,
        Status = DocumentStatus.InXero,
        Settled = settled,
        Document = Samples.Completed(Samples.NewInvoice()) with
        {
            Customer = new Customer { Name = customer },
            Total = total,
        },
    };

    [Fact]
    public void Statements_are_offered_in_the_first_week()
    {
        Assert.True(Statements.IsStatementWeek(new DateOnly(2026, 11, 1)));
        Assert.True(Statements.IsStatementWeek(new DateOnly(2026, 11, 7)));
        Assert.False(Statements.IsStatementWeek(new DateOnly(2026, 11, 8)));
    }

    [Fact]
    public void Customers_with_open_invoices_are_due_once_a_month()
    {
        var documents = new[]
        {
            Invoice("1", "Karoo Coffee", 1000), Invoice("2", "karoo coffee ", 500),
            Invoice("3", "Lerato Designs", 300), Invoice("4", "Paid Up", 900, settled: DateTimeOffset.Now),
        };
        var due = Statements.Due(documents, new Dictionary<string, DateOnly>(), Today);
        Assert.Equal(2, due.Count);
        Assert.Equal(("Karoo Coffee", 1500m), due[0]);

        var sent = Statements.MarkSent(new Dictionary<string, DateOnly>(), "Karoo Coffee", Today);
        Assert.Equal("Lerato Designs", Assert.Single(Statements.Due(documents, sent, Today)).Customer);

        // Last month's statement doesn't count for this month.
        var lastMonth = Statements.MarkSent(new Dictionary<string, DateOnly>(), "Karoo Coffee", new DateOnly(2026, 10, 2));
        Assert.Equal(2, Statements.Due(documents, lastMonth, Today).Count);
    }

    [Fact]
    public void Old_entries_are_dropped_when_marking()
    {
        var old = new Dictionary<string, DateOnly> { ["gone"] = Today.AddYears(-2) };
        var sent = Statements.MarkSent(old, "Karoo Coffee", Today);
        Assert.False(sent.ContainsKey("gone"));
        Assert.Equal(Today, sent["karoo coffee"]);
    }

    [Fact]
    public void Lines_list_unpaid_invoices_oldest_first()
    {
        StatementLine[] lines =
        [
            new("INV-0112", new(2026, 10, 20), new(2026, 11, 20), 500, 500, null),
            new("INV-0101", new(2026, 9, 1), new(2026, 10, 2), 1000, 400, "Hosting"),
            new("INV-0090", new(2026, 8, 1), new(2026, 9, 1), 200, 0, null),
            new("INV-0105", new(2026, 10, 1), new(2026, 11, 2), 100, 100, null),
        ];
        var text = Statements.Lines(lines, Today).Split('\n');
        Assert.Equal(3, text.Length);
        Assert.Equal($"- INV-0101, 1 Sep 2026, Hosting: {Money.Rand(400)}, 32 days overdue", text[0]);
        Assert.Equal($"- INV-0105, 1 Oct 2026: {Money.Rand(100)}, 1 day overdue", text[1]);
        Assert.Equal($"- INV-0112, 20 Oct 2026: {Money.Rand(500)}, due 20 Nov 2026", text[2]);
        Assert.Equal(1000m, Statements.TotalDue(lines));
    }

    [Fact]
    public void The_statement_mail_fills_in()
    {
        var filled = DocumentEmail.Fill(new EmailTemplates().Statement, new DocumentEmailValues
        {
            Kind = DocumentKind.Invoice,
            ContactName = "Karoo Coffee",
            FirstName = "Thandi",
            Number = "",
            Total = 900,
            AmountDue = 900,
            StatementLines = "- INV-0101: R 900.00",
            StatementDate = Today,
            TradingName = "Webignite",
        });
        Assert.Empty(filled.Unfilled);
        Assert.Equal("Statement from Webignite for Karoo Coffee", filled.Subject);
        Assert.Contains("as at 3 November 2026", filled.Body);
        Assert.Contains("- INV-0101: R 900.00", filled.Body);
    }

    [Fact]
    public void A_linked_contact_is_used_first()
    {
        var clients = new[] { new Client { Id = "c", Name = "Karoo Coffee", XeroContactId = "from-client" } };
        var documents = new[] { Invoice("1", "Karoo Coffee", 10) with { XeroContactId = "from-invoice" } };
        Assert.Equal("from-client", Statements.ContactIdFor("karoo coffee", documents, clients));
        Assert.Equal("from-invoice", Statements.ContactIdFor("Karoo Coffee", documents, []));
        Assert.Null(Statements.ContactIdFor("Someone Else", documents, clients));
    }

    [Fact]
    public void The_morning_summary_offers_statements_in_the_first_week()
    {
        var input = new SummaryInput { Today = Today, Documents = [Invoice("1", "Karoo Coffee", 1000)] };
        var line = Assert.Single(MorningSummary.Build(input), l => l.Target == SummaryTarget.Statement);
        Assert.Equal("Karoo Coffee", line.Customer);
        Assert.DoesNotContain(MorningSummary.Build(input with { Today = new DateOnly(2026, 11, 12) }), l => l.Target == SummaryTarget.Statement);
    }
}
