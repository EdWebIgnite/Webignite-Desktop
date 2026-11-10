using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Projects;
using Webignite.Core.Sync;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>Quotes, invoices, clients and contacts going between the two PCs through the real sync server, in memory.</summary>
public sealed class FinanceSyncTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-finance-sync-").FullName;
    private readonly WebApplicationFactory<Program> server;
    private readonly List<IDisposable> made = [];

    public FinanceSyncTests()
    {
        server = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Webignite:Database"] = Path.Combine(folder, "server.db"),
                ["Webignite:SignInPerMinute"] = "1000",
            }));
        });
    }

    public void Dispose()
    {
        foreach (var thing in Enumerable.Reverse(made))
            thing.Dispose();
        server.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private sealed record Pc(LocalStore Store, AccountClient Account, SyncService Sync);

    private Pc NewPc(string name, string myName)
    {
        var store = new LocalStore(Path.Combine(folder, name + ".db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        made.Add(store);
        store.SetSetting("team", ProjectRules.Ensure(new TeamSettings(), myName));
        var account = new AccountClient(store, new MemorySecretStore(), server.Server.CreateHandler());
        made.Add(account);
        Assert.Null(account.UseServer("http://localhost"));
        var sync = new SyncService(store, account);
        made.Add(sync);
        return new Pc(store, account, sync);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<(Pc Owner, Pc Member)> TwoPcs()
    {
        var owner = NewPc("eddie", "Eddie");
        Assert.True((await owner.Account.SignUpAsync("eddie@webignite.co.za", "karoo rooibos sunrise", "Webignite", null, Cancel)).Worked);
        var invite = await owner.Account.InviteAsync(Cancel);
        var member = NewPc("jaco", "Jaco");
        Assert.True((await member.Account.SignUpAsync("jaco@webignite.co.za", "fynbos table mountain", null, invite.Value!.Code, Cancel)).Worked);
        // The owner's PC goes first, so both know who's who.
        await Sync(owner);
        await Sync(member);
        return (owner, member);
    }

    private static async Task Sync(Pc pc)
    {
        var result = await pc.Sync.SyncAsync(Cancel);
        Assert.True(result.Worked, result.Problem);
    }

    private static StoredDocument Quote(string customer) => new()
    {
        Id = StoredDocument.NewId(),
        Document = Samples.Completed(Samples.NewInvoice() with { Kind = DocumentKind.Quote, Customer = new Customer { Name = customer } }),
    };

    [Fact]
    public async Task A_quote_client_and_contact_made_on_one_pc_show_on_the_other()
    {
        var (owner, member) = await TwoPcs();
        var quote = owner.Store.Save(Quote("Karoo Coffee Roasters"));
        var client = owner.Store.SaveClient(new Client { Id = Client.NewId(), Name = "Karoo Coffee Roasters", Folder = @"C:\Clients\Karoo" });
        var person = owner.Store.SaveContact(new Person { Id = Person.NewId(), Name = "Thandi Mokoena", ClientId = client.Id, Changed = DateTimeOffset.UtcNow });
        await Sync(owner);
        await Sync(member);

        Assert.Equal("Karoo Coffee Roasters", member.Store.Get(quote.Id)!.Document.Customer.Name);
        Assert.Equal("Karoo Coffee Roasters", member.Store.GetClient(client.Id)!.Name);
        Assert.Equal(client.Id, member.Store.GetContact(person.Id)!.ClientId);
        // The folder is a path on Eddie's PC; it doesn't come along.
        Assert.Null(member.Store.GetClient(client.Id)!.Folder);
    }

    [Fact]
    public async Task A_change_on_the_other_pc_comes_back_without_undoing_that_it_was_emailed_here()
    {
        var (owner, member) = await TwoPcs();
        var quote = owner.Store.Save(Quote("Fynbos Physio"));
        await Sync(owner);
        await Sync(member);

        // Eddie emails it; a moment later Jaco changes the reference on his PC, from his older copy.
        owner.Store.Update(quote.Id, q => q with { Delivery = q.Delivery with { Emailed = DateTimeOffset.UtcNow, EmailedTo = "info@fynbosphysio.co.za" } });
        await Task.Delay(20, Cancel);
        member.Store.Update(quote.Id, q => q with { Document = q.Document with { Reference = "Booking page" } });
        await Sync(owner);
        await Sync(member);
        await Sync(owner);

        foreach (var pc in new[] { owner, member })
        {
            var now = pc.Store.Get(quote.Id)!;
            Assert.Equal("Booking page", now.Document.Reference);
            Assert.NotNull(now.Delivery.Emailed);
            Assert.Equal("info@fynbosphysio.co.za", now.Delivery.EmailedTo);
        }
    }

    [Fact]
    public async Task A_deletion_reaches_the_other_pc_and_a_later_change_there_brings_it_back()
    {
        var (owner, member) = await TwoPcs();
        var draft = owner.Store.Save(Quote("Bosveld Builders"));
        var client = owner.Store.SaveClient(new Client { Id = Client.NewId(), Name = "Bosveld Builders" });
        await Sync(owner);
        await Sync(member);

        owner.Store.DeleteDocument(draft.Id);
        await Sync(owner);
        await Sync(member);
        Assert.Null(member.Store.Get(draft.Id));

        // Deleted on Eddie's PC, then changed later on Jaco's: the change wins, so nothing he did is lost.
        owner.Store.DeleteClient(client.Id);
        await Task.Delay(20, Cancel);
        member.Store.SaveClient(member.Store.GetClient(client.Id)! with { Phone = "012 345 6789" });
        await Sync(owner);
        await Sync(member);
        await Sync(owner);
        Assert.Equal("012 345 6789", owner.Store.GetClient(client.Id)!.Phone);
    }
}
