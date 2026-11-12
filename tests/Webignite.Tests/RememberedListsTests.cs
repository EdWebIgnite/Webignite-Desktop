using System.Security.Cryptography;
using Webignite.Core.Clients;
using Webignite.Core.Sync;
using Webignite.Data;

namespace Webignite.Tests;

/// <summary>Lists are unlocked once and handed out again, but never older than the database.</summary>
public sealed class RememberedListsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

    private LocalStore Open() => new(Path.Combine(folder, "r.db"), new Vault(key));

    private static StoredDocument Invoice(string customer) => new()
    {
        Id = StoredDocument.NewId(),
        Document = Samples.Completed(Samples.NewInvoice() with { Customer = new Customer { Name = customer } }),
    };

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void A_list_read_twice_with_nothing_changed_is_the_same_list()
    {
        using var store = Open();
        store.Save(Invoice("Karoo Coffee Roasters"));

        Assert.Same(store.List(), store.List());
        Assert.Same(store.ListClients(), store.ListClients());
        Assert.Same(store.ListTasks(), store.ListTasks());
    }

    [Fact]
    public void A_write_here_shows_in_the_next_read()
    {
        using var store = Open();
        store.Save(Invoice("Karoo Coffee Roasters"));
        Assert.Single(store.List());

        var second = store.Save(Invoice("Fynbos Physio"));
        Assert.Equal(2, store.List().Count);
        Assert.Single(store.List(DocumentKind.Invoice), d => d.Id == second.Id);

        store.Update(second.Id, d => d with { Status = DocumentStatus.InXero });
        Assert.Equal(DocumentStatus.InXero, store.List().Single(d => d.Id == second.Id).Status);

        store.DeleteDocument(second.Id);
        Assert.Single(store.List());

        var client = store.SaveClient(new Client { Id = "c1", Name = "Karoo Coffee Roasters" });
        Assert.Single(store.ListClients());
        store.UpdateClient(client.Id, c => c with { Email = "info@karoocoffee.co.za" });
        Assert.Equal("info@karoocoffee.co.za", store.ListClients().Single().Email);
    }

    [Fact]
    public void A_write_on_another_thread_shows_in_the_next_read()
    {
        using var store = Open();
        store.Save(Invoice("Karoo Coffee Roasters"));
        Assert.Single(store.List());

        var writer = new Thread(() => store.Save(Invoice("Fynbos Physio")));
        writer.Start();
        writer.Join();

        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void A_write_by_another_program_shows_in_the_next_read()
    {
        using var store = Open();
        store.Save(Invoice("Karoo Coffee Roasters"));
        Assert.Single(store.List());

        // The connector Claude and ChatGPT use is its own program with the same database open.
        using (var connector = Open())
            connector.Save(Invoice("Fynbos Physio"));

        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void A_change_that_was_undone_part_way_is_not_remembered()
    {
        using var store = Open();
        var kept = store.Save(Invoice("Karoo Coffee Roasters"));
        Assert.Single(store.List());

        Assert.Throws<InvalidOperationException>(() => store.Update(kept.Id, _ =>
        {
            store.SaveClient(new Client { Id = "c1", Name = "Never saved" });
            Assert.Single(store.ListClients()); // seen inside, while it is still being written
            throw new InvalidOperationException("stopped part way");
        }));

        Assert.Empty(store.ListClients());
        Assert.Single(store.List());
    }

    [Fact]
    public void Lists_nobody_asks_for_are_let_go_and_read_again_when_wanted()
    {
        using var store = Open();
        store.Save(Invoice("Karoo Coffee Roasters"));
        var first = store.List();

        store.LetGoOfUnused(TimeSpan.FromHours(1));
        Assert.Same(first, store.List());

        store.LetGoOfUnused(TimeSpan.Zero);
        var again = store.List();
        Assert.NotSame(first, again);
        Assert.Equal(first.Single().Id, again.Single().Id);
    }

    [Fact]
    public void Only_the_pages_that_show_what_came_in_read_again()
    {
        Assert.Equal(["chat"], SyncKinds.Areas([SyncKinds.Chat, SyncKinds.ChatPart, SyncKinds.Presence]));
        Assert.Equal(["projects"], SyncKinds.Areas([SyncKinds.Task, SyncKinds.Comment, SyncKinds.Message]));
        Assert.Equal(["documents", "clients"], SyncKinds.Areas([SyncKinds.Document]));
        Assert.Equal(["projects", "contacts", "settings"], SyncKinds.Areas([SyncKinds.Setting, SyncKinds.Contact, SyncKinds.Heading]));
        Assert.Empty(SyncKinds.Areas([]));
        // The team changes ids in everything, and a kind this version doesn't know could be shown anywhere.
        Assert.Equal(SyncKinds.AllAreas, SyncKinds.Areas([SyncKinds.Team]));
        Assert.Equal(SyncKinds.AllAreas, SyncKinds.Areas(["something new"]));
        // Every kind that syncs is covered.
        Assert.All(SyncKinds.All, kind => Assert.NotEmpty(SyncKinds.Areas(kind)));
    }
}
