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
