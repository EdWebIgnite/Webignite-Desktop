using System.Security.Cryptography;
using Webignite.Core.Assistants;
using Webignite.Core.Projects;
using Webignite.Core.Writing;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

public sealed class AssistantSafetyTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private readonly LocalStore store;
    private readonly AssistantPlanner planner;
    private readonly AssistantBusiness business;

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public AssistantSafetyTests()
    {
        store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        planner = new AssistantPlanner(store, new FixedClock(), Path.Combine(folder, "attachments"));
        business = new AssistantBusiness(store, new FixedClock());
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private AssistantPutBack PutBack() => new(store, new ProjectService(store, () => "me", new FixedClock(), Path.Combine(folder, "attachments")),
        new ClientService(store), new ContactService(store), new NoteService(store));

    [Fact]
    public void What_they_do_is_stamped_as_theirs_and_listed()
    {
        planner.Who = "ChatGPT";
        planner.CreateProject("Shop");
        var task = planner.AddTask("Shop", "Product pages");
        planner.PostStatus("Shop", "on track", "Going well.");
        planner.Noted("Added the task Product pages to Shop");

        Assert.Equal(Actors.ChatGpt, new ProjectService(store, () => "me").GetTask(task.Task.Id)!.Created.By);
        Assert.Equal("ChatGPT", planner.GetProject("Shop").LatestUpdate!.By);
        var entry = Assert.Single(AssistantLog.List(store));
        Assert.Equal(("ChatGPT", "Added the task Product pages to Shop"), (entry.Who, entry.What));
        Assert.False(entry.CanPutBack);
    }

    [Fact]
    public void A_replaced_description_is_kept_with_its_formatting_and_can_be_put_back()
    {
        planner.CreateProject("Shop");
        var task = planner.AddTask("Shop", "Product pages", notes: "Old notes");
        var projects = new ProjectService(store, () => "me");
        projects.ChangeTask(task.Task.Id, t => t with { NotesHtml = "<p><b>Old notes</b></p>" });

        planner.UpdateTask(task.Task.Id, notes: "New notes");
        Assert.Equal("New notes", projects.GetTask(task.Task.Id)!.Notes);
        Assert.Throws<ArgumentException>(() => planner.UpdateTask(task.Task.Id, notes: " "));

        var entry = Assert.Single(AssistantLog.List(store), e => e.CanPutBack);
        Assert.Null(PutBack().Apply(entry));
        var back = projects.GetTask(task.Task.Id)!;
        Assert.Equal(("Old notes", "<p><b>Old notes</b></p>"), (back.Notes, back.NotesHtml));
        Assert.False(Assert.Single(AssistantLog.List(store), e => e.Id == entry.Id).CanPutBack);
    }

    [Fact]
    public void A_client_email_or_name_changed_by_them_waits_for_a_check_and_can_be_put_back()
    {
        business.AddClient("Karoo Coffee Roasters", email: "accounts@karoo.co.za");
        var changed = business.UpdateClient("Karoo", email: "accounts@kar00.co.za");
        Assert.Equal("accounts@kar00.co.za", changed.Client.Email);

        var clients = new ClientService(store);
        var stored = clients.Find("Karoo Coffee Roasters")!;
        Assert.NotNull(stored.Check);
        Assert.Contains("accounts@karoo.co.za", stored.Check!.What, StringComparison.Ordinal);
        Assert.Contains(stored.Log, n => n.Text.Contains("kar00", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => business.UpdateClient("Karoo", email: ""));

        Assert.Null(PutBack().Apply(AssistantLog.List(store).First(e => e.Field == "client-email")));
        stored = clients.Find("Karoo Coffee Roasters")!;
        Assert.Equal("accounts@karoo.co.za", stored.Email);
        Assert.Null(stored.Check);
    }

    [Fact]
    public void A_billing_contact_email_changed_by_them_marks_the_client_too()
    {
        business.AddClient("Bosveld Builders");
        var person = business.AddContact("Marlene Botha", email: "marlene@bosveld.co.za", client: "Bosveld");
        var clients = new ClientService(store);
        var client = clients.Find("Bosveld Builders")!;
        clients.Change(client.Id, c => c with { BillingContactId = person.Id });

        business.UpdateContact(person.Id, email: "accounts@b0sveld.co.za");
        Assert.NotNull(clients.Get(client.Id)!.Check);
    }

    [Fact]
    public void A_replaced_note_comes_back_with_its_formatting_and_notes_are_never_emptied()
    {
        var note = business.AddNote("Kick-off", "Goals");
        var notes = new NoteService(store);
        var writing = notes.Writing(note.Note.Id);
        var bold = new TextBlock();
        bold.Append(RichText.Inline("**Important**"));
        writing.Blocks.Insert(0, bold);
        notes.Save(note.Note.Id, "Kick-off", writing);

        business.UpdateNote("Kick-off", text: "Something else");
        Assert.Throws<ArgumentException>(() => business.UpdateNote("Kick-off", text: ""));
        Assert.Null(PutBack().Apply(AssistantLog.List(store).First(e => e.Field == "note")));
        Assert.True(notes.Writing(note.Note.Id).Blocks.OfType<TextBlock>().First().Runs[0].Format.Bold);
    }

    [Fact]
    public void Writing_is_limited_in_size()
    {
        planner.CreateProject("Shop");
        Assert.Throws<ArgumentException>(() => planner.AddTask("Shop", new string('x', AssistantLimits.Title + 1)));
        Assert.Throws<ArgumentException>(() => business.AddNote("Big", new string('x', AssistantLimits.Text + 1)));
        Assert.Equal("", AssistantLimits.Emptying("", "", "a note"));
        Assert.NotEqual("", AssistantLimits.Emptying("Something", "  ", "a note"));
    }
}
