using System.Security.Cryptography;
using Webignite.Core.Clients;
using Webignite.Core.Writing;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

public sealed class AssistantBusinessTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private readonly LocalStore store;
    private readonly AssistantBusiness business;

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public AssistantBusinessTests()
    {
        store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        business = new AssistantBusiness(store, new FixedClock());
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Clients_are_added_once_changed_and_noted_but_their_logins_never_show()
    {
        var added = business.AddClient("Karoo Coffee Roasters", contactPerson: "Anna", email: "anna@karoo.co.za", hourlyRate: 650m);
        var again = business.AddClient("karoo coffee roasters");
        Assert.Equal(added.Client.Id, again.Client.Id);
        Assert.Single(business.ListClients());

        store.SaveSiteLogin(new SiteLogin { Id = "l", ClientId = added.Client.Id, Site = "WordPress", UserName = "anna", Password = "secret-pass" });
        var changed = business.UpdateClient("karoo", phone: "082 555 0100", website: "");
        var noted = business.AddClientNote("Karoo", "Wants a shop by March.", "Claude", "2026-10-30");

        Assert.Equal("082 555 0100", changed.Client.Phone);
        Assert.Equal("Anna", changed.Client.ContactPerson);
        Assert.Equal(new BusinessNote("2026-10-30", "Wants a shop by March. (Claude)"), new BusinessNote(noted.Log[0].Date, noted.Log[0].Text));
        Assert.DoesNotContain("secret-pass", System.Text.Json.JsonSerializer.Serialize(noted), StringComparison.Ordinal);
        Assert.Contains("clients", AssistantSignal.Read(store)!.Areas);
    }

    private sealed record BusinessNote(string Date, string Text);

    [Fact]
    public void People_are_added_once_by_email_and_a_new_email_becomes_their_main_one()
    {
        business.AddClient("Bosveld Builders");
        var pieter = business.AddContact("Pieter van Wyk", email: "pieter@bosveld.co.za", client: "bosveld", groups: ["Builders"]);
        var again = business.AddContact("P van Wyk", email: "PIETER@bosveld.co.za");
        Assert.Equal(pieter.Id, again.Id);
        Assert.Equal("Bosveld Builders", pieter.Client);

        var changed = business.UpdateContact("pieter@bosveld.co.za", email: "accounts@bosveld.co.za", role: "Owner");
        Assert.Equal(["accounts@bosveld.co.za", "pieter@bosveld.co.za"], changed.Emails);
        Assert.Equal("Owner", changed.Role);
        Assert.Single(business.ListContacts(client: "Bosveld Builders"));
        Assert.Single(business.ListContacts(search: "builders"));
    }

    [Fact]
    public void Notes_are_written_read_and_added_to_without_losing_their_formatting()
    {
        business.AddClient("Karoo Coffee Roasters");
        var note = business.AddNote("Kick-off", "Goals\n- More online orders\n[ ] Get the logo", client: "karoo");
        Assert.Equal("Karoo Coffee Roasters", note.Note.Client);
        Assert.Contains("[ ] Get the logo", note.Text, StringComparison.Ordinal);

        // Bold writing in the note, as typed in the app, stays bold when an assistant adds to the end.
        var notes = new NoteService(store);
        var writing = notes.Writing(note.Note.Id);
        var bold = new TextBlock();
        bold.Append(RichText.Inline("**Important**"));
        writing.Blocks.Insert(0, bold);
        notes.Save(note.Note.Id, "Kick-off", writing);

        var added = business.UpdateNote("kick-off", append: "Follow up on Friday.", pinned: true);
        Assert.True(added.Note.Pinned);
        Assert.EndsWith("Follow up on Friday.", added.Text.TrimEnd(), StringComparison.Ordinal);
        var kept = notes.Writing(note.Note.Id).Blocks.OfType<TextBlock>().First();
        Assert.True(kept.Runs[0].Format.Bold);
        Assert.Single(business.ListNotes(search: "online orders"));
    }

    [Fact]
    public void Events_are_added_moved_and_made_all_day()
    {
        var call = business.AddEvent("Call with Karoo", "2026-11-04", start: "09:30");
        Assert.Equal("10:30", call.End);

        var moved = business.UpdateEvent(call.Id, date: "2026-11-05", start: "14:00");
        Assert.Equal(("2026-11-05", "14:00", "15:00"), (moved.Date, moved.Start, moved.End));
        var allDay = business.UpdateEvent("Call with Karoo", start: "none");
        Assert.Null(allDay.Start);
        Assert.Null(allDay.End);
        Assert.Single(business.ListEvents("2026-11-01", "2026-11-30"));
        Assert.Throws<ArgumentException>(() => business.AddEvent("Late", "2026-11-04", start: "15:00", end: "14:00"));
    }

    [Fact]
    public void Time_is_logged_for_a_client_and_their_project()
    {
        var client = business.AddClient("Karoo Coffee Roasters");
        store.UpdateClient(client.Client.Id, c => c with { Projects = [new ClientProject { Id = "p", Name = "Shop" }] });

        var logged = business.LogTime("1:30", "Product pages", client: "karoo", project: "shop", date: "2026-11-01");
        Assert.Equal((90, "Karoo Coffee Roasters", "Shop"), (logged.Minutes, logged.Client, logged.Project));
        Assert.Single(business.ListTime(client: "karoo"));
        Assert.Throws<ArgumentException>(() => business.LogTime("forever", "Nothing"));
        Assert.Throws<ArgumentException>(() => business.LogTime("1h", "No client", project: "Shop"));
    }

    [Fact]
    public void Renewals_are_added_and_changed_by_the_same_rules_as_the_app()
    {
        business.AddClient("Karoo Coffee Roasters");
        var domain = business.AddRenewal("karoocoffee.co.za", "2027-01-15", client: "karoo", provider: "xneelo", cost: 99m, price: 250m);
        Assert.Equal(("Domain", "Karoo Coffee Roasters", 12), (domain.Kind, domain.Client, domain.PeriodMonths));

        var changed = business.UpdateRenewal("karoocoffee", renewsOn: "2027-02-15", kind: "hosting", price: 300m);
        Assert.Equal(("2027-02-15", "Hosting", 300m), (changed.RenewsOn, changed.Kind, changed.Price));
        Assert.Throws<ArgumentException>(() => business.AddRenewal("nobody.co.za", "2027-01-01", price: 100m));
        Assert.Single(business.GetClient("Karoo").Renewals);
    }
}
