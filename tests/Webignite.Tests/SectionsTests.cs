using Webignite.Core.Layout;

namespace Webignite.Tests;

/// <summary>Folding rows on a page: their order and which are open, remembered.</summary>
public sealed class SectionsTests
{
    private static readonly string[] Keys = ["todo", "documents", "mail", "people"];

    [Fact]
    public void With_nothing_saved_the_rows_are_in_their_usual_order_with_the_usual_ones_open()
    {
        var layout = Sections.Reconcile(null, Keys, ["todo", "documents", "gone"]);
        Assert.Equal(Keys, layout.Order);
        Assert.Equal(["todo", "documents"], layout.Open);
    }

    [Fact]
    public void A_saved_layout_keeps_its_order_drops_old_rows_and_adds_new_ones_closed_at_the_bottom()
    {
        var saved = new SectionLayout { Order = ["mail", "old", "todo"], Open = ["old", "mail"] };
        var layout = Sections.Reconcile(saved, Keys, ["todo"]);
        Assert.Equal(["mail", "todo", "documents", "people"], layout.Order);
        Assert.Equal(["mail"], layout.Open);
    }

    [Fact]
    public void Rows_open_and_close_one_at_a_time_or_all_together()
    {
        var layout = Sections.Reconcile(null, Keys, []);
        layout = Sections.Toggle(layout, "mail");
        Assert.True(layout.IsOpen("mail"));
        Assert.False(Sections.Toggle(layout, "mail").IsOpen("mail"));
        Assert.Equal(Keys, Sections.All(layout, true).Open);
        Assert.Empty(Sections.All(layout, false).Open);
    }

    [Fact]
    public void A_row_moves_where_it_is_dropped_or_one_step_at_a_time()
    {
        var layout = Sections.Reconcile(null, Keys, []);
        Assert.Equal(["mail", "todo", "documents", "people"], Sections.MoveTo(layout, "mail", 0).Order);
        Assert.Equal(["documents", "mail", "todo", "people"], Sections.MoveTo(layout, "todo", 3).Order);
        Assert.Equal(["documents", "mail", "people", "todo"], Sections.MoveTo(layout, "todo", 4).Order);
        Assert.Equal(["todo", "mail", "documents", "people"], Sections.MoveBy(layout, "mail", -1).Order);
        Assert.Equal(["documents", "todo", "mail", "people"], Sections.MoveBy(layout, "todo", 1).Order);
        Assert.Equal(Keys, Sections.MoveBy(layout, "todo", -1).Order);
        Assert.Equal(Keys, Sections.MoveBy(layout, "people", 1).Order);
    }
}
