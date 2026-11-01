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
