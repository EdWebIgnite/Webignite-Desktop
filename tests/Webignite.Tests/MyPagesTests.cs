using Webignite.Core.Layout;

namespace Webignite.Tests;

/// <summary>The person's own pages: widgets on a fine grid, left where they're put, moved and resized without overlapping.</summary>
public sealed class MyPagesTests
{
    private static MyPage Empty => new() { Id = "p", Name = "Mine" };

    private static Widget At(MyPage page, int index) => page.Widgets[index];

    private static bool NoOverlaps(MyPage page) =>
        page.Widgets.All(a => page.Widgets.All(b => a.Id == b.Id || !MyPages.Overlap(a, b)));

    [Fact]
    public void A_note_keeps_its_formatting_and_its_plain_words()
    {
        var page = MyPages.Add(Empty, WidgetKind.Note);
        var id = At(page, 0).Id;

        page = MyPages.SetNote(page, id, "Call Pieter", "<p><b>Call</b> Pieter</p><ul><li>photos</li></ul>");

        Assert.Equal("Call Pieter", At(page, 0).Text);
        var note = MyPages.NoteOf(At(page, 0));
        Assert.Contains("Call Pieter", Webignite.Core.Writing.RichText.ToText(note), StringComparison.Ordinal);
        Assert.Contains("<b>", Webignite.Core.Writing.RichHtml.Write(note, Webignite.Core.Writing.RichHtml.ToDataAddress), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(note.Blocks.OfType<Webignite.Core.Writing.TextBlock>(), b => b.List == Webignite.Core.Writing.ListKind.Bullet);
    }

    [Fact]
    public void A_note_typed_before_formatting_came_in_is_read_from_its_words()
    {
        var old = new Widget { Id = "w", Kind = WidgetKind.Note, Text = "Line one\nLine two" };

        Assert.Equal("Line one\nLine two", Webignite.Core.Writing.RichText.ToText(MyPages.NoteOf(old)).TrimEnd());
        Assert.Equal("", Webignite.Core.Writing.RichText.ToText(MyPages.NoteOf(new Widget { Id = "e", Kind = WidgetKind.Note })).TrimEnd());
    }

    [Fact]
    public void Pages_are_listed_loose_ones_first_then_by_client_then_by_project()
    {
        MyPage Page(string id, string? client = null, string? project = null) => new() { Id = id, Name = id, ClientId = client, ProjectId = project };
        var clients = new Dictionary<string, string> { ["c1"] = "Modic Digital", ["c2"] = "G3" };
        var projects = new Dictionary<string, string> { ["p1"] = "Amos website", ["p2"] = "App fixes" };

        var groups = MyPages.Grouped(
            [Page("Monday"), Page("Forus", "c1"), Page("Amos", "c2", "p1"), Page("Internal", project: "p2"), Page("Kemin", "c2"), Page("Orphan", "gone"), Page("Lost", project: "gone")],
            id => clients.GetValueOrDefault(id), id => projects.GetValueOrDefault(id));

        Assert.Equal([PageGroupKind.None, PageGroupKind.Client, PageGroupKind.Client, PageGroupKind.Project], groups.Select(g => g.Kind));
        // Pages for no one, and those whose client or project is gone, are loose.
        Assert.Equal(["Monday", "Orphan", "Lost"], groups[0].Pages.Select(p => p.Name));
        Assert.Equal(["G3", "Modic Digital", "App fixes"], groups.Skip(1).Select(g => g.Name));
        // A page for a client and a project is under its client.
        Assert.Equal(["Amos", "Kemin"], groups[1].Pages.Select(p => p.Name));
        Assert.Equal("p2", groups[3].Id);
    }

    [Fact]
    public void With_no_pages_for_anyone_there_is_just_the_one_loose_group()
    {
        var groups = MyPages.Grouped([Empty], _ => null, _ => null);

        Assert.Equal(PageGroupKind.None, Assert.Single(groups).Kind);
        Assert.Single(groups[0].Pages);
    }

    [Theory]
    [InlineData("Modic Digital", "Forus website", "For Modic Digital · Forus website")]
    [InlineData("Modic Digital", null, "For Modic Digital")]
    [InlineData(null, "App fixes", "For App fixes")]
    [InlineData(null, null, "")]
    public void A_page_says_who_it_is_for(string? client, string? project, string text) => Assert.Equal(text, MyPages.ForText(client, project));

    [Fact]
    public void A_page_can_be_for_a_client_a_project_both_or_neither()
    {
        var page = MyPages.For(Empty, "c1", "p1");
        Assert.Equal(("c1", "p1"), (page.ClientId, page.ProjectId));
        page = MyPages.For(page, null, " ");
        Assert.Null(page.ClientId);
        Assert.Null(page.ProjectId);
    }

    [Fact]
    public void New_widgets_go_side_by_side_then_onto_the_next_row()
    {
        var page = Empty;
        for (var i = 0; i < 4; i++)
            page = MyPages.Add(page, WidgetKind.MyTasks);
        Assert.Equal([(0, 0), (16, 0), (32, 0), (0, 16)], page.Widgets.Select(w => (w.Column, w.Row)));
        Assert.True(NoOverlaps(page));
    }

    [Fact]
    public void A_new_widget_fills_a_gap_it_fits_in()
    {
        var page = MyPages.Add(MyPages.Add(Empty, WidgetKind.Project), WidgetKind.Timer);
        Assert.Equal((24, 0), (At(page, 1).Column, At(page, 1).Row));
    }

    [Fact]
    public void Dropping_a_widget_onto_another_nudges_that_one_down()
    {
        var page = MyPages.Add(MyPages.Add(Empty, WidgetKind.MyTasks), WidgetKind.Today);
        page = MyPages.Move(page, At(page, 1).Id, 8, 0);
        Assert.Equal((8, 0), (At(page, 1).Column, At(page, 1).Row));
        Assert.Equal(16, At(page, 0).Row);
        Assert.True(NoOverlaps(page));
    }

    [Fact]
    public void Dragging_a_widget_down_past_another_swaps_them()
    {
        var page = MyPages.Add(MyPages.Add(Empty, WidgetKind.MyTasks), WidgetKind.Today);
        page = MyPages.Move(page, At(page, 1).Id, 0, 0);
        Assert.Equal(16, At(page, 0).Row);
        page = MyPages.Move(page, At(page, 1).Id, 0, 20);
        Assert.Equal(4, At(page, 0).Row);
        Assert.Equal(20, At(page, 1).Row);
        Assert.True(NoOverlaps(page));
    }

    [Fact]
    public void Widgets_stay_where_they_are_put_gaps_and_all()
    {
        var page = MyPages.Add(MyPages.Add(Empty, WidgetKind.MyTasks), WidgetKind.Today);
        page = MyPages.Move(page, At(page, 1).Id, 5, 37);
        Assert.Equal((5, 37), (At(page, 1).Column, At(page, 1).Row));
        page = MyPages.Remove(page, At(page, 0).Id);
        Assert.Equal((5, 37), (At(page, 0).Column, At(page, 0).Row));
    }

    [Fact]
    public void A_widget_stays_inside_the_columns()
    {
        var page = MyPages.Add(Empty, WidgetKind.MyTasks);
        page = MyPages.Move(page, At(page, 0).Id, 45, -3);
        Assert.Equal((32, 0), (At(page, 0).Column, At(page, 0).Row));
    }

    [Fact]
    public void Resizing_keeps_between_the_least_size_and_the_edge()
    {
        var page = MyPages.Add(Empty, WidgetKind.Calculator);
        page = MyPages.Move(page, At(page, 0).Id, 24, 0);
        page = MyPages.Resize(page, At(page, 0).Id, 1, 1);
        Assert.Equal((12, 20), (At(page, 0).Width, At(page, 0).Height));
        page = MyPages.Resize(page, At(page, 0).Id, 48, 160);
        Assert.Equal((24, MyPages.MostHeight), (At(page, 0).Width, At(page, 0).Height));
    }

    [Fact]
    public void Growing_a_widget_pushes_the_one_below_down()
    {
        var page = MyPages.Add(Empty, WidgetKind.MyTasks);
        page = MyPages.Add(page, WidgetKind.MyTasks);
        page = MyPages.Move(page, At(page, 1).Id, 0, 16);
        page = MyPages.Resize(page, At(page, 0).Id, 16, 24);
        Assert.Equal(24, At(page, 1).Row);
        Assert.True(NoOverlaps(page));
    }

    [Fact]
    public void A_page_from_the_twelve_column_grid_is_scaled_up_and_made_sound()
    {
        var page = Empty with
        {
            Name = " ",
            Widgets =
            [
                new Widget { Id = "a", Kind = WidgetKind.Note, Column = 10, Row = 3, Width = 6, Height = 0 },
                new Widget { Id = "b", Kind = WidgetKind.Note, Column = 6, Row = 3, Width = 6, Height = 3 },
            ],
        };
        var sound = MyPages.Reconcile(page);
        Assert.Equal("My page", sound.Name);
        Assert.Equal(MyPages.Columns, sound.Grid);
        Assert.Equal((24, 8), (sound.Widgets[0].Column, sound.Widgets[0].Height));
        Assert.Equal((24, 12), (sound.Widgets[1].Width, sound.Widgets[1].Height));
        Assert.True(NoOverlaps(sound));
        Assert.Equal(12, sound.Widgets.Min(w => w.Row));

        // Read again, a page already on the fine grid is left as it is.
        Assert.Equal(sound.Widgets, MyPages.Reconcile(sound).Widgets);
        Assert.Equal(MyPages.Columns, MyPages.NewPage([]).Grid);
    }

    [Fact]
    public void The_files_widget_opens_on_its_folder_else_the_client_folders_else_documents()
    {
        var there = new HashSet<string> { @"C:\Projects", @"C:\Projects\Bosveld" };
        Assert.Equal(@"C:\Projects\Bosveld", MyPages.StartFolder(@"C:\Projects\Bosveld", there.Contains, @"C:\Projects", "Docs"));
        Assert.Equal(@"C:\Projects", MyPages.StartFolder(@"C:\Gone", there.Contains, @"C:\Projects", "Docs"));
        Assert.Equal("Docs", MyPages.StartFolder(null, there.Contains, null, "Docs"));
        var page = MyPages.Add(Empty, WidgetKind.Files);
        page = MyPages.SetTarget(page, page.Widgets[0].Id, "D:\\Clients");
        Assert.Equal("D:\\Clients", Assert.Single(page.Widgets).Target);
    }

    [Fact]
    public void New_pages_get_a_name_not_yet_taken()
    {
        var first = MyPages.NewPage([]);
        var second = MyPages.NewPage([first]);
        Assert.Equal("My page", first.Name);
        Assert.Equal("My page 2", second.Name);
        Assert.Equal("Clients", MyPages.Rename(first, "  Clients ").Name);
        Assert.Equal("My page", MyPages.Rename(first, " ").Name);
    }

    [Fact]
    public void Projects_and_clients_are_picked_when_added()
    {
        Assert.True(MyPages.NeedsTarget(WidgetKind.Project));
        Assert.True(MyPages.NeedsTarget(WidgetKind.Client));
        Assert.False(MyPages.NeedsTarget(WidgetKind.Calculator));
        var page = MyPages.Add(Empty, WidgetKind.Project, "proj1");
        Assert.Equal("proj1", At(page, 0).Target);
    }
}
