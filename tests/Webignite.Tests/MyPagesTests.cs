using Webignite.Core.Layout;

namespace Webignite.Tests;

/// <summary>The person's own pages: widgets on a twelve-column grid, moved and resized without overlapping.</summary>
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
        Assert.Equal([(0, 0), (4, 0), (8, 0), (0, 4)], page.Widgets.Select(w => (w.Column, w.Row)));
        Assert.True(NoOverlaps(page));
    }

    [Fact]
    public void A_new_widget_fills_a_gap_it_fits_in()
    {
        var page = MyPages.Add(MyPages.Add(Empty, WidgetKind.Project), WidgetKind.Timer);
        Assert.Equal((6, 0), (At(page, 1).Column, At(page, 1).Row));
    }

    [Fact]
    public void Dropping_a_widget_onto_another_pushes_that_one_down()
    {
        var page = MyPages.Add(MyPages.Add(Empty, WidgetKind.MyTasks), WidgetKind.Today);
        page = MyPages.Move(page, At(page, 1).Id, 2, 0);
        Assert.Equal((2, 0), (At(page, 1).Column, At(page, 1).Row));
        Assert.Equal(4, At(page, 0).Row);
        Assert.True(NoOverlaps(page));
    }

    [Fact]
    public void Dragging_a_widget_down_past_another_swaps_them()
    {
        var page = MyPages.Add(MyPages.Add(Empty, WidgetKind.MyTasks), WidgetKind.Today);
        page = MyPages.Move(page, At(page, 1).Id, 0, 0);
        Assert.Equal(0, At(page, 1).Row);
        Assert.Equal(4, At(page, 0).Row);
        page = MyPages.Move(page, At(page, 1).Id, 0, 5);
        Assert.Equal(0, At(page, 0).Row);
        Assert.Equal(4, At(page, 1).Row);
    }

    [Fact]
    public void Widgets_slide_up_into_room_left_above_them()
    {
        var page = MyPages.Add(MyPages.Add(Empty, WidgetKind.MyTasks), WidgetKind.Today);
        page = MyPages.Move(page, At(page, 1).Id, 0, 9);
        Assert.Equal((0, 4), (At(page, 1).Column, At(page, 1).Row));
        page = MyPages.Remove(page, At(page, 0).Id);
        Assert.Equal(0, At(page, 0).Row);
    }

    [Fact]
    public void A_widget_stays_inside_the_columns()
    {
        var page = MyPages.Add(Empty, WidgetKind.MyTasks);
        page = MyPages.Move(page, At(page, 0).Id, 11, -3);
        Assert.Equal((8, 0), (At(page, 0).Column, At(page, 0).Row));
    }

    [Fact]
    public void Resizing_keeps_between_the_least_size_and_the_edge()
    {
        var page = MyPages.Add(Empty, WidgetKind.Calculator);
        page = MyPages.Move(page, At(page, 0).Id, 6, 0);
        page = MyPages.Resize(page, At(page, 0).Id, 1, 1);
        Assert.Equal((3, 5), (At(page, 0).Width, At(page, 0).Height));
        page = MyPages.Resize(page, At(page, 0).Id, 12, 40);
        Assert.Equal((6, MyPages.MostHeight), (At(page, 0).Width, At(page, 0).Height));
    }

    [Fact]
    public void Growing_a_widget_pushes_the_one_below_down()
    {
        var page = MyPages.Add(Empty, WidgetKind.MyTasks);
        page = MyPages.Add(page, WidgetKind.MyTasks);
        page = MyPages.Move(page, At(page, 1).Id, 0, 4);
        page = MyPages.Resize(page, At(page, 0).Id, 4, 6);
        Assert.Equal(6, At(page, 1).Row);
        Assert.True(NoOverlaps(page));
    }

    [Fact]
    public void A_saved_page_with_overlaps_or_odd_sizes_is_made_sound()
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
        Assert.Equal((6, 2), (sound.Widgets[0].Column, sound.Widgets[0].Height));
        Assert.True(NoOverlaps(sound));
        Assert.Equal(0, sound.Widgets.Min(w => w.Row));
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
