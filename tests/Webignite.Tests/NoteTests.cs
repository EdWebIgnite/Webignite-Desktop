using System.Security.Cryptography;
using Webignite.Core.Notes;
using Webignite.Core.Writing;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>Notes: checklists in the writing, the rules for lists, folders and search, and the note service.</summary>
public sealed class NoteTests : IDisposable
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAYAAAC56t6BAAAAEklEQVR4nGP4z8DwH4QZYAwAR8oH+Rq28akAAAAASUVORK5CYII=");

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 30, 0, TimeSpan.FromHours(2));

    private readonly string folder = Directory.CreateTempSubdirectory("webignite-notes-").FullName;
    private readonly LocalStore store;
    private readonly ManualClock clock = new(Now);

    public NoteTests() => store = new LocalStore(Path.Combine(folder, "webignite.db"), new Vault(RandomNumberGenerator.GetBytes(32)));

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private static Note Note(string id, string title = "", string text = "", string notebook = "b1", string folder = "", DateTimeOffset? changed = null) =>
        new() { Id = id, NotebookId = notebook, Title = title, Text = text, Folder = folder, Changed = changed ?? Now };

    private static RichDocument Checklist(params (string Text, bool Ticked)[] items)
    {
        var d = new RichDocument();
        foreach (var (text, ticked) in items)
            d.Blocks.Add(new TextBlock(text) { List = ListKind.Check, Checked = ticked });
        return d;
    }

    // Checklists in the writing

    [Fact]
    public void A_checklist_reads_and_writes_as_plain_text()
    {
        var d = RichText.FromText("Shopping\n[ ] Milk\n[x] Bread\n- [ ] Eggs");
        var items = d.Blocks.OfType<TextBlock>().Where(b => b.List == ListKind.Check).ToList();
        Assert.Equal(["Milk", "Bread", "Eggs"], items.Select(b => b.Text));
        Assert.Equal([false, true, false], items.Select(b => b.Checked));
        Assert.Equal("Shopping\n[ ] Milk\n[x] Bread\n[ ] Eggs", RichText.ToText(d));
    }

    [Fact]
    public void A_checklist_keeps_its_ticks_through_html()
    {
        var html = RichHtml.Write(Checklist(("Send the logo", true), ("Book the shoot", false)), RichHtml.ToDataAddress);
        Assert.Contains("☑ Send the logo", html, StringComparison.Ordinal);
        Assert.Contains("☐ Book the shoot", html, StringComparison.Ordinal);

        var back = RichHtml.Read(html).Blocks.OfType<TextBlock>().ToList();
        Assert.Equal(["Send the logo", "Book the shoot"], back.Select(b => b.Text));
        Assert.All(back, b => Assert.Equal(ListKind.Check, b.List));
        Assert.Equal([true, false], back.Select(b => b.Checked));
    }

    [Fact]
    public void An_empty_checklist_item_survives_html()
    {
        var back = RichHtml.Read(RichHtml.Write(Checklist(("", false)), RichHtml.ToDataAddress));
        var item = Assert.IsType<TextBlock>(Assert.Single(back.Blocks));
        Assert.Equal(ListKind.Check, item.List);
        Assert.Equal("", item.Text);
    }

    [Fact]
    public void Enter_after_a_ticked_item_starts_an_unticked_one()
    {
        var d = Checklist(("Done thing", true));
        var at = RichEditing.Enter(d, new DocPosition(0, 10));
        var next = Assert.IsType<TextBlock>(d.Blocks[at.Slot]);
        Assert.Equal(ListKind.Check, next.List);
        Assert.False(next.Checked);
        Assert.True(((TextBlock)d.Blocks[0]).Checked);
    }

    [Fact]
    public void Turning_lines_into_a_checklist_starts_them_unticked()
    {
        var d = Checklist(("a", true));
        RichEditing.ToggleList(d, new DocPosition(0, 0), new DocPosition(0, 1), ListKind.Bullet);
        Assert.False(((TextBlock)d.Blocks[0]).Checked);
        Assert.Equal(ListKind.Bullet, ((TextBlock)d.Blocks[0]).List);
    }

    [Fact]
    public void A_ticked_item_stays_ticked_in_a_copy()
    {
        var copy = Checklist(("a", true)).Clone();
        Assert.True(((TextBlock)copy.Blocks[0]).Checked);
    }

    // Rules

    [Fact]
    public void A_note_is_named_by_its_title_or_first_line()
    {
        Assert.Equal("Meeting", NoteRules.Name(Note("1", title: " Meeting ")));
        Assert.Equal("Call Pieter back", NoteRules.Name(Note("2", text: "\n  Call Pieter back\nabout the shop")));
        Assert.Equal("Milk", NoteRules.Name(Note("3", text: "[ ] Milk")));
        Assert.Equal(NoteRules.Untitled, NoteRules.Name(Note("4")));
    }

    [Fact]
    public void The_preview_skips_the_line_used_as_the_name()
    {
        Assert.Equal("about the shop and the logo", NoteRules.Preview(Note("1", text: "Call Pieter\nabout the shop\n\nand the logo")));
        Assert.Equal("Call Pieter about the shop", NoteRules.Preview(Note("2", title: "Karoo", text: "Call Pieter\nabout the shop")));
        Assert.Equal("Milk Bread Eggs", NoteRules.Preview(Note("3", title: "Shopping", text: "[ ] Milk\n[x] Bread\n- Eggs")));
    }

    [Fact]
    public void Describing_a_note_counts_its_checklist()
    {
        var note = NoteRules.Describe(Note("1"), Checklist(("a", true), ("b", false), ("c", true)));
        Assert.Equal(3, note.Checks);
        Assert.Equal(2, note.Ticked);
        Assert.Equal("2 of 3 done", NoteRules.Progress(note));
        Assert.Equal("", NoteRules.Progress(Note("2")));
    }

    [Fact]
    public void Pinned_notes_come_first_then_the_newest()
    {
        var ordered = NoteRules.Ordered([
            Note("old", changed: Now.AddDays(-3)),
            Note("new", changed: Now),
            Note("pinned", changed: Now.AddDays(-9)) with { Pinned = true },
        ]);
        Assert.Equal(["pinned", "new", "old"], ordered.Select(n => n.Id));
    }

    [Fact]
    public void Each_place_shows_its_own_notes_and_deleted_ones_only_in_deleted()
    {
        Note[] notes =
        [
            Note("a"),
            Note("b", folder: "Meetings"),
            Note("c", notebook: "b2"),
            Note("d") with { ClientId = "karoo" },
            Note("e") with { Deleted = Now, ClientId = "karoo" },
        ];
        Assert.Equal(["a", "b", "c", "d"], NoteRules.In(notes, NotePlace.All).Select(n => n.Id));
        Assert.Equal(["a", "b", "d"], NoteRules.In(notes, NotePlace.Notebook, "b1").Select(n => n.Id));
        Assert.Equal(["b"], NoteRules.In(notes, NotePlace.Folder, "b1", "meetings").Select(n => n.Id));
        Assert.Equal(["d"], NoteRules.In(notes, NotePlace.Client, clientId: "karoo").Select(n => n.Id));
        Assert.Equal(["e"], NoteRules.In(notes, NotePlace.Deleted).Select(n => n.Id));
    }

    [Fact]
    public void Deleted_notes_go_after_thirty_days()
    {
        Assert.False(NoteRules.Expired(Note("a") with { Deleted = Now.AddDays(-29) }, Now));
        Assert.True(NoteRules.Expired(Note("a") with { Deleted = Now.AddDays(-30) }, Now));
        Assert.False(NoteRules.Expired(Note("a"), Now));
    }

    [Fact]
    public void A_quick_note_takes_its_first_line_as_the_title()
    {
        var (note, writing) = NoteRules.Quick("Karoo call\nWants the shop\n[ ] Send a quote", "q1", "b1", Now);
        Assert.Equal("Karoo call", note.Title);
        Assert.Equal(1, note.Checks);
        Assert.Equal("Wants the shop\n[ ] Send a quote", RichText.ToText(writing));

        var (single, _) = NoteRules.Quick("Buy printer ink", "q2", "b1", Now);
        Assert.Equal("", single.Title);
        Assert.Equal("Buy printer ink", NoteRules.Name(single));

        var (list, _) = NoteRules.Quick("[ ] one\n[ ] two", "q3", "b1", Now);
        Assert.Equal("", list.Title);
        Assert.Equal(2, list.Checks);
    }

    [Fact]
    public void Search_needs_every_word_and_ranks_title_matches_first()
    {
        Note[] notes =
        [
            Note("body", text: "We spoke to Lerato about the Karoo logo"),
            Note("title", title: "Karoo logo ideas", changed: Now.AddDays(-5)),
            Note("other", text: "Fynbos physio"),
        ];
        var hits = NoteRules.Search(notes, "karoo LOGO");
        Assert.Equal(["title", "body"], hits.Select(h => h.Note.Id));
        Assert.Empty(NoteRules.Search(notes, "karoo shop"));
        Assert.Empty(NoteRules.Search(notes, "  "));
    }

    [Fact]
    public void Search_ignores_accents_and_shows_where_it_matched()
    {
        var long_ = new string('x', 200) + " Lérato said yes " + new string('y', 200);
        var hit = Assert.Single(NoteRules.Search([Note("a", title: "Calls", text: long_)], "lerato"));
        Assert.Contains("Lérato said yes", hit.Snippet, StringComparison.Ordinal);
        Assert.StartsWith("…", hit.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Folder_names_must_be_new_and_not_empty()
    {
        var notebook = new Notebook { Id = "b1", Name = "Work", Folders = ["Meetings"] };
        Assert.Null(NoteRules.NewFolder(notebook, " meetings "));
        Assert.Null(NoteRules.NewFolder(notebook, "  "));
        Assert.Equal("Ideas", NoteRules.NewFolder(notebook, " Ideas\n"));
    }

    [Fact]
    public void Renaming_a_folder_takes_its_notes_along()
    {
        var notebook = new Notebook { Id = "b1", Name = "Work", Folders = ["Meetings", "Ideas"] };
        var (renamed, moved) = NoteRules.RenameFolder(notebook, [Note("a", folder: "Meetings"), Note("b", folder: "Ideas"), Note("c", notebook: "b2", folder: "Meetings")], "Meetings", "Calls");
        Assert.Equal(["Calls", "Ideas"], renamed.Folders);
        Assert.Equal("a", Assert.Single(moved).Id);
        Assert.Equal("Calls", moved[0].Folder);
        Assert.Empty(NoteRules.RenameFolder(notebook, [], "Meetings", "ideas").Moved);
    }

    [Fact]
    public void Removing_a_folder_keeps_its_notes_in_the_notebook()
    {
        var notebook = new Notebook { Id = "b1", Name = "Work", Folders = ["Meetings"] };
        var (kept, moved) = NoteRules.RemoveFolder(notebook, [Note("a", folder: "Meetings")], "Meetings");
        Assert.Empty(kept.Folders);
        Assert.Equal("", Assert.Single(moved).Folder);
    }

    [Fact]
    public void A_new_notebook_gets_a_free_name()
    {
        Assert.Equal("Notebook", NoteRules.NextNotebookName([new Notebook { Id = "1", Name = "My notes" }]));
        Assert.Equal("Notebook 3", NoteRules.NextNotebookName([new Notebook { Id = "1", Name = "notebook" }, new Notebook { Id = "2", Name = "Notebook 2" }]));
    }

    // The service

    [Fact]
    public void There_is_always_a_notebook()
    {
        var notes = new NoteService(store, clock);
        var first = Assert.Single(notes.Notebooks());
        Assert.Equal(NoteRules.DefaultNotebook, first.Name);
        Assert.Single(notes.Notebooks());
    }

    [Fact]
    public void A_note_keeps_its_writing_with_pictures_tables_and_ticks()
    {
        var notes = new NoteService(store, clock);
        var note = notes.New(notes.Notebooks()[0].Id, clientId: "karoo");
        var writing = Checklist(("Logo", true), ("Shop", false));
        writing.Blocks.Insert(0, new TextBlock("Plan") { Style = BlockStyle.Heading1 });
        writing.Blocks.Add(new PictureBlock(Png, "image/png", 2, 3));
        var table = TableBlock.Empty(2, 2);
        table.Rows[0][0].Insert(0, "Page", default);
        writing.Blocks.Add(table);
        clock.Now = Now.AddMinutes(5);

        var saved = notes.Save(note.Id, "Karoo website", writing)!;

        Assert.Equal("Karoo website", saved.Title);
        Assert.Equal(Now.AddMinutes(5), saved.Changed);
        Assert.Equal((2, 1), (saved.Checks, saved.Ticked));
        Assert.Equal("karoo", saved.ClientId);
        var back = notes.Writing(note.Id);
        Assert.Equal(BlockStyle.Heading1, ((TextBlock)back.Blocks[0]).Style);
        Assert.True(((TextBlock)back.Blocks[1]).Checked);
        Assert.Equal(Png, Assert.Single(back.Blocks.OfType<PictureBlock>()).Data);
        Assert.Equal("Page", Assert.Single(back.Blocks.OfType<TableBlock>()).Rows[0][0].Text);
    }

    [Fact]
    public void Pinning_elsewhere_doesnt_undo_typing_and_typing_doesnt_undo_the_pin()
    {
        var notes = new NoteService(store, clock);
        var note = notes.New(notes.Notebooks()[0].Id);
        notes.Change(note.Id, n => n with { Pinned = true });
        notes.Save(note.Id, "Typed", RichText.FromText("words"));
        var stored = notes.Get(note.Id)!;
        Assert.True(stored.Pinned);
        Assert.Equal("Typed", stored.Title);
    }

    [Fact]
    public void A_new_note_says_so()
    {
        var notes = new NoteService(store, clock);
        var told = new List<string?>();
        notes.Changed += told.Add;
        var note = notes.New(notes.Notebooks()[0].Id);
        Assert.Equal([note.Id], told);
    }

    [Fact]
    public void Deleted_notes_wait_then_go()
    {
        var notes = new NoteService(store, clock);
        var note = notes.New(notes.Notebooks()[0].Id);
        notes.Change(note.Id, n => n with { Pinned = true });
        var deleted = notes.Delete(note.Id)!;
        Assert.NotNull(deleted.Deleted);
        Assert.False(deleted.Pinned);
        Assert.Null(notes.Restore(note.Id)!.Deleted);

        notes.Delete(note.Id);
        clock.Now = Now.AddDays(31);
        Assert.Empty(notes.List());
        Assert.Null(notes.Get(note.Id));
        Assert.Equal("", store.NoteBody(note.Id));
    }

    [Fact]
    public void Emptying_deleted_keeps_the_rest()
    {
        var notes = new NoteService(store, clock);
        var book = notes.Notebooks()[0].Id;
        var keep = notes.New(book);
        var gone = notes.New(book);
        notes.Delete(gone.Id);
        notes.EmptyDeleted();
        Assert.Equal(keep.Id, Assert.Single(notes.List()).Id);
    }

    [Fact]
    public void A_quick_note_goes_into_the_notebook_asked_or_the_first()
    {
        var notes = new NoteService(store, clock);
        var first = notes.Notebooks()[0];
        clock.Now = Now.AddSeconds(1);
        var work = notes.AddNotebook();
        Assert.Equal(work.Id, notes.Quick("Call the bank", work.Id).NotebookId);
        Assert.Equal(first.Id, notes.Quick("Call the bank", "gone").NotebookId);
        Assert.Equal("Call the bank", RichText.ToText(notes.Writing(notes.List()[0].Id)));
    }

    [Fact]
    public void A_notebook_with_notes_or_the_last_one_stays()
    {
        var notes = new NoteService(store, clock);
        var first = notes.Notebooks()[0];
        Assert.False(notes.RemoveNotebook(first.Id));
        clock.Now = Now.AddSeconds(1);
        var second = notes.AddNotebook();
        var note = notes.New(second.Id);
        Assert.False(notes.RemoveNotebook(second.Id));
        notes.Delete(note.Id);
        Assert.True(notes.RemoveNotebook(second.Id));
        Assert.Null(notes.Get(note.Id));
        Assert.Equal(first.Id, Assert.Single(notes.Notebooks()).Id);
    }

    [Fact]
    public void Folders_rename_and_go_with_their_notes()
    {
        var notes = new NoteService(store, clock);
        var book = notes.Notebooks()[0].Id;
        Assert.Equal("Meetings", notes.AddFolder(book, "Meetings"));
        Assert.Null(notes.AddFolder(book, "meetings"));
        var note = notes.New(book, "Meetings");
        notes.RenameFolder(book, "Meetings", "Calls");
        Assert.Equal("Calls", notes.Get(note.Id)!.Folder);
        Assert.Equal(["Calls"], notes.Notebooks()[0].Folders);
        notes.RemoveFolder(book, "Calls");
        Assert.Equal("", notes.Get(note.Id)!.Folder);
        Assert.Empty(notes.Notebooks()[0].Folders);
    }

    [Fact]
    public void Saving_a_note_deleted_for_good_meanwhile_saves_nothing()
    {
        var notes = new NoteService(store, clock);
        var note = notes.New(notes.Notebooks()[0].Id);
        notes.Purge(note.Id);
        Assert.Null(notes.Save(note.Id, "late", RichText.FromText("typing")));
        Assert.Null(notes.Get(note.Id));
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }
}
