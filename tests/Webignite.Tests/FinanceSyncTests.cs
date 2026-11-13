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

/// <summary>Quotes, invoices, clients, contacts and chat going between the two PCs through the real sync server, in memory.</summary>
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

    [Fact]
    public async Task A_chat_message_with_a_screenshot_reaches_the_other_pc_whole_and_counts_as_unread_there()
    {
        var (owner, member) = await TwoPcs();
        var eddie = new ChatService(owner.Store);
        var jaco = new ChatService(member.Store);
        var picture = Enumerable.Range(0, Webignite.Core.Chat.ChatRules.PartBytes + 5000).Select(i => (byte)(i % 199)).ToArray();
        var sent = eddie.Send("Here's the homepage", [("Screenshot.png", picture)])!;
        await Sync(owner);
        await Sync(member);

        var arrived = Assert.Single(jaco.Messages());
        Assert.Equal("Here's the homepage", arrived.Text);
        Assert.Equal(picture, jaco.FileBytes(arrived.Files[0]));
        Assert.Equal("You", eddie.NameOf(sent.From));
        Assert.Equal(1, jaco.Unread);
        Assert.Single(jaco.NewlyArrived());
        Assert.Empty(jaco.NewlyArrived()); // told once
        jaco.MarkRead();
        Assert.Equal(0, jaco.Unread);
        Assert.Equal(0, eddie.Unread); // his own message isn't unread for him
    }

    [Fact]
    public async Task A_pictures_data_arriving_counts_as_news_so_the_chat_is_drawn_again_with_the_picture()
    {
        var (owner, member) = await TwoPcs();
        var eddie = new ChatService(owner.Store);
        var picture = Enumerable.Range(0, Webignite.Core.Chat.ChatRules.PartBytes + 5000).Select(i => (byte)(i % 199)).ToArray();
        eddie.Send("", [("funny.jpg", picture)]);
        await Sync(owner);

        // The message and each of the picture's two parts are news: any of them may come in a sync of its own.
        var arrived = await member.Sync.SyncAsync(Cancel);
        Assert.True(arrived.Worked, arrived.Problem);
        Assert.Equal(3, arrived.Changed);
        Assert.Equal(0, (await member.Sync.SyncAsync(Cancel)).Changed); // and only once
    }

    [Fact]
    public async Task The_business_settings_reach_the_partner_and_a_change_there_comes_back_but_looks_stay_on_each_pc()
    {
        var (eddie, jaco) = await TwoPcs();
        eddie.Store.SetSetting("documents", new DocumentSettings { InvoiceDueDays = 14 });
        eddie.Store.SetSetting("price-list", new List<string> { "Hosting, 12 months" });
        eddie.Store.SetSetting("appearance", "dark");
        await Sync(eddie);
        await Sync(jaco);
        Assert.Equal(14, jaco.Store.GetSetting<DocumentSettings>("documents")!.InvoiceDueDays);
        Assert.Equal(["Hosting, 12 months"], jaco.Store.GetSetting<List<string>>("price-list")!);
        Assert.Null(jaco.Store.GetSetting<string>("appearance"));

        await Task.Delay(20, Cancel);
        jaco.Store.SetSetting("documents", jaco.Store.GetSetting<DocumentSettings>("documents")! with { QuoteExpiryDays = 60 });
        await Sync(jaco);
        await Sync(eddie);
        var back = eddie.Store.GetSetting<DocumentSettings>("documents")!;
        Assert.Equal((14, 60), (back.InvoiceDueDays, back.QuoteExpiryDays));

        // Nothing goes back and forth once both have the same.
        Assert.Equal(0, (await eddie.Sync.SyncAsync(Cancel)).Changed);
        Assert.Equal(0, (await jaco.Sync.SyncAsync(Cancel)).Changed);
    }

    [Fact]
    public async Task My_status_reaches_my_partner_with_each_sync()
    {
        var (eddie, jaco) = await TwoPcs();
        var eddieChat = new ChatService(eddie.Store);
        var jacoChat = new ChatService(jaco.Store);
        eddie.Sync.MyPresence = () => eddieChat.Mine();
        jaco.Sync.PresenceArrived = p => jacoChat.TakeIn(p);
        Assert.Equal(Webignite.Core.Chat.PresenceState.Offline, jacoChat.PartnerShown);

        eddieChat.Chosen = Webignite.Core.Chat.PresenceState.Busy;
        await Sync(eddie);
        await Sync(jaco);
        Assert.Equal(Webignite.Core.Chat.PresenceState.Busy, jacoChat.PartnerShown);

        eddieChat.Chosen = Webignite.Core.Chat.PresenceState.Available;
        await Sync(eddie);
        await Sync(jaco);
        Assert.Equal(Webignite.Core.Chat.PresenceState.Available, jacoChat.PartnerShown);
    }

    [Fact]
    public async Task The_name_each_person_gives_themselves_reaches_the_other_pc()
    {
        var (eddie, jaco) = await TwoPcs();
        // Eddie's PC still calls his partner "Partner"; Jaco's own name comes over with Jaco's next sync.
        Assert.Equal(ProjectRules.DefaultPartner, eddie.Store.GetSetting<TeamSettings>("team")!.Partner!.Name);
        await Sync(jaco);
        await Sync(eddie);
        var team = eddie.Store.GetSetting<TeamSettings>("team")!;
        Assert.Equal("Jaco", team.Partner!.Name);
        Assert.Equal("jaco@webignite.co.za", team.Partner.Email);

        // Jaco renames himself: Eddie's PC follows.
        var jacoTeam = jaco.Store.GetSetting<TeamSettings>("team")!;
        jaco.Store.SetSetting("team", jacoTeam with { Me = jacoTeam.Me! with { Name = "Jaco van Wyk" } });
        await Sync(jaco);
        await Sync(eddie);
        Assert.Equal("Jaco van Wyk", eddie.Store.GetSetting<TeamSettings>("team")!.Partner!.Name);
        Assert.Equal("Eddie", jaco.Store.GetSetting<TeamSettings>("team")!.Partner!.Name);
    }
}
