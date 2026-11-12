using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Webignite.Core.Calendar;
using Webignite.Core.Clients;
using Webignite.Core.Tasks;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class ClientTests : IDisposable
{
    private static readonly DateOnly Today = Samples.Today;
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private static readonly Client Karoo = new() { Id = "c1", Name = "Karoo Coffee Roasters" };

    private static StoredDocument Doc(string customer, DocumentStatus status = DocumentStatus.InXero) => new()
    {
        Id = StoredDocument.NewId(),
        Status = status,
        Document = Samples.Completed(Samples.NewInvoice() with { Customer = new Customer { Name = customer } }),
    };

    [Fact]
    public void Names_match_ignoring_case_and_spaces()
    {
        Assert.True(ClientDirectory.Matches(Karoo, "  karoo coffee   ROASTERS "));
        Assert.False(ClientDirectory.Matches(Karoo, "Karoo Coffee"));
        Assert.False(ClientDirectory.Matches(Karoo with { Name = "" }, ""));
    }

    [Fact]
    public void Summary_gathers_documents_tasks_and_upcoming_events()
    {
        var summary = ClientDirectory.Summarise(Karoo,
            [Doc("Karoo Coffee Roasters"), Doc("karoo coffee roasters", DocumentStatus.Draft), Doc("Someone Else")],
            [
                new TaskItem { Id = "t1", Title = "Call", Customer = "Karoo Coffee Roasters" },
                new TaskItem { Id = "t2", Title = "Done already", Customer = "Karoo Coffee Roasters", Done = true },
            ],
            [
                new CalendarEvent { Id = "e1", Title = "Visit", Date = Today.AddDays(2), Customer = "Karoo Coffee Roasters" },
                new CalendarEvent { Id = "e2", Title = "Past", Date = Today.AddDays(-2), Customer = "Karoo Coffee Roasters" },
            ],
            Today);

        Assert.Equal(2, summary.Documents.Count);
        Assert.Equal(450m, summary.Owed); // only the one in Xero
        Assert.Equal("Call", Assert.Single(summary.OpenTasks).Title);
        Assert.Equal("Visit", Assert.Single(summary.UpcomingEvents).Title);
    }

    [Fact]
    public void Summarising_every_client_at_once_gives_what_one_at_a_time_gives()
    {
        var fynbos = new Client { Id = "c2", Name = "Fynbos  Physio" };
        var nameless = new Client { Id = "c3", Name = " " };
        StoredDocument[] documents = [Doc("Karoo Coffee Roasters"), Doc("karoo coffee roasters", DocumentStatus.Draft), Doc("fynbos physio"), Doc(" ")];
        TaskItem[] tasks =
        [
            new() { Id = "t1", Title = "Call", Customer = "Karoo Coffee Roasters" },
            new() { Id = "t2", Title = "Done already", Customer = "Karoo Coffee Roasters", Done = true },
            new() { Id = "t3", Title = "Nobody's" },
        ];
        CalendarEvent[] events =
        [
            new() { Id = "e1", Title = "Visit", Date = Today.AddDays(2), Customer = "FYNBOS PHYSIO" },
            new() { Id = "e2", Title = "Past", Date = Today.AddDays(-2), Customer = "Fynbos Physio" },
        ];

        var together = ClientDirectory.SummariseAll([Karoo, fynbos, nameless], documents, tasks, events, Today);

        Assert.Equal(3, together.Count);
        foreach (var summary in together)
        {
            var alone = ClientDirectory.Summarise(summary.Client, documents, tasks, events, Today);
            Assert.Equal(alone.Documents.Select(d => d.Id), summary.Documents.Select(d => d.Id));
            Assert.Equal(alone.OpenTasks.Select(t => t.Id), summary.OpenTasks.Select(t => t.Id));
            Assert.Equal(alone.UpcomingEvents.Select(e => e.Id), summary.UpcomingEvents.Select(e => e.Id));
        }
        Assert.Equal(2, together[0].Documents.Count);
        Assert.Empty(together[2].Documents);
    }

    [Fact]
    public void Customers_on_documents_without_a_client_page_are_offered()
    {
        var names = ClientDirectory.UnsavedNames([Karoo], [Doc("Karoo Coffee Roasters"), Doc("Fynbos Physio"), Doc("fynbos physio"), Doc(" ")]);
        Assert.Equal(["Fynbos Physio"], names);
    }

    [Fact]
    public void Clients_save_list_sorted_and_delete()
    {
        using var store = new LocalStore(Path.Combine(folder, "c.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        store.SaveClient(new Client { Id = "b", Name = "bosveld Builders" });
        store.SaveClient(Karoo with { Email = "accounts@karoo.example" });
        store.SaveClient(new Client { Id = "a", Name = "Aloe Spa" });

        Assert.Equal(["Aloe Spa", "bosveld Builders", "Karoo Coffee Roasters"], store.ListClients().Select(c => c.Name));
        store.DeleteClient("a");
        Assert.Equal(2, store.ListClients().Count);
    }

    [Fact]
    public void Check_needs_a_name_and_a_unique_one()
    {
        Assert.Equal(["Give the client a name."], ClientDirectory.Check(Karoo with { Name = "  " }, []));
        var copy = new Client { Id = "c2", Name = "karoo coffee roasters" };
        Assert.Equal(["There is already a client called karoo coffee roasters."], ClientDirectory.Check(copy, [Karoo]));
        Assert.Empty(ClientDirectory.Check(Karoo, [Karoo]));
    }

    [Theory]
    [InlineData("accounts@webignite.co.za", true)]
    [InlineData("hello@karoo", false)]
    [InlineData("two@@at.co.za", false)]
    [InlineData("no at.co.za", false)]
    public void Check_spots_a_wrong_email(string email, bool fine) =>
        Assert.Equal(fine, ClientDirectory.Check(Karoo with { Email = email }, []).Count == 0);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }
}

/// <summary>A client can have several email addresses, read the same way everywhere.</summary>
public sealed class ClientEmailTests
{
    private static readonly Webignite.Core.Clients.Client Karoo = new()
    {
        Id = "c1", Name = "Karoo Coffee Roasters", ContactPerson = "Thandi Mokoena",
        Email = "thandi@karoocoffee.co.za; accounts.karoo@gmail.com",
    };

    [Fact]
    public void Several_addresses_can_be_saved_and_a_wrong_one_is_named()
    {
        Assert.Empty(Webignite.Core.Clients.ClientDirectory.Check(Karoo, []));
        Assert.Equal(["\"accounts@\" doesn't look like an email address."],
            Webignite.Core.Clients.ClientDirectory.Check(Karoo with { Email = "thandi@karoocoffee.co.za; accounts@" }, []));
    }

    [Fact]
    public void Every_address_finds_the_client()
    {
        Assert.Equal(Karoo, Webignite.Core.Mail.SavedReplies.ClientFor([Karoo], "accounts.karoo@gmail.com"));
        Assert.Equal(Karoo, Webignite.Core.Overview.MorningSummary.ClientFor("accounts.karoo@gmail.com", [Karoo]));
        Assert.Equal(Karoo, Webignite.Core.Overview.MorningSummary.ClientFor("pieter@karoocoffee.co.za", [Karoo]));
        // Gmail is shared, so another Gmail sender is nobody's client.
        Assert.Null(Webignite.Core.Mail.SavedReplies.ClientFor([Karoo], "someone@gmail.com"));
        Assert.Equal("Thandi", Webignite.Core.Mail.SavedReplies.FirstName(
            new Webignite.Core.Mail.MailPerson("Accounts", "accounts.karoo@gmail.com"), Karoo));
    }

    [Fact]
    public void Where_only_one_fits_the_first_is_used()
    {
        Assert.Equal("thandi@karoocoffee.co.za", Webignite.Core.Mail.ClientMail.First(Karoo.Email));
        Assert.Null(Webignite.Core.Mail.ClientMail.First(""));
        var contact = new Webignite.Core.Xero.XeroContact("k1", "Karoo Coffee Roasters", "thandi@karoocoffee.co.za", true);
        Assert.IsType<Webignite.Core.Xero.ContactMatch.Use>(Webignite.Core.Xero.XeroRules.MatchContact("Karoo Coffee Roasters", Karoo.Email, [contact]));
        Assert.Equal("thandi@karoocoffee.co.za",
            Webignite.Xero.XeroPayload.NewContact(new Customer { Name = "Karoo", Email = Karoo.Email }, null)["EmailAddress"]!.GetValue<string>());
    }
}

/// <summary>Names and websites are each compared one way, everywhere.</summary>
public sealed class OneRuleTests
{
    [Fact]
    public void Names_match_ignoring_case_and_any_extra_spaces()
    {
        Assert.Equal("karoo coffee roasters", Webignite.Core.Clients.ClientDirectory.Key("  Karoo Coffee \t ROASTERS "));
        var karoo = new Webignite.Core.Clients.Client { Id = "c1", Name = "Karoo Coffee Roasters" };
        Assert.Equal(karoo, Webignite.Core.Clients.ClientDirectory.Find([karoo], "karoo  coffee roasters"));
        Assert.Null(Webignite.Core.Clients.ClientDirectory.Find([karoo], ""));
    }

    [Fact]
    public void A_customer_typed_two_ways_gets_one_statement_line()
    {
        StoredDocument Owing(string name, decimal due) => new()
        {
            Id = StoredDocument.NewId(),
            Status = DocumentStatus.InXero,
            Document = Samples.Completed(Samples.NewInvoice() with { Customer = new Customer { Name = name } }),
            Xero = new()
            {
                Id = StoredDocument.NewId(),
                AmountDue = due,
            },
        };
        var due = Statements.Due([Owing("Karoo Coffee", 100m), Owing("karoo  coffee", 50m)], new Dictionary<string, DateOnly>(), Samples.Today);
        Assert.Equal(150m, Assert.Single(due).Open);
    }

    [Theory]
    [InlineData("https://www.Karoo.co.za/menu", "karoo.co.za")]
    [InlineData("karoocoffee.co.za", "karoocoffee.co.za")]
    [InlineData("thandi@karoocoffee.co.za", "karoocoffee.co.za")]
    [InlineData("not a website", null)]
    public void A_website_has_one_domain(string website, string? domain) =>
        Assert.Equal(domain, Webignite.Core.Web.Domains.Of(website));
}
