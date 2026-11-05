using System.Text.Json;
using Webignite.Core.Layout;

namespace Webignite.Tests;

public sealed class PanelsTests
{
    private static readonly PanelLayout Mail = Panels.Default("folders", "messages", "reader");

    [Fact]
    public void The_middle_pane_can_be_hidden_too_leaving_the_ones_either_side()
    {
        var layout = Panels.Show(Mail, "messages", false);
        Assert.Equal(["folders", "reader"], Panels.Visible(layout));
        Assert.True(Panels.CanHide(layout, "folders"));
        Assert.Equal(["folders", "messages", "reader"], Panels.Visible(Panels.Show(layout, "messages", true)));
    }

    [Fact]
    public void Panes_can_be_hidden_and_shown_but_not_all_of_them()
    {
        var layout = Panels.Show(Mail, "folders", false);
        Assert.Equal(["messages", "reader"], Panels.Visible(layout));
        Assert.False(layout.IsShown("folders"));

        layout = Panels.Show(layout, "reader", false);
        Assert.Equal(["messages"], Panels.Visible(layout));
        Assert.False(Panels.CanHide(layout, "messages"));
        Assert.Same(layout, Panels.Show(layout, "messages", false));

        layout = Panels.Show(layout, "folders", true);
        Assert.Equal(["folders", "messages"], Panels.Visible(layout));
    }

    [Fact]
    public void Moving_swaps_with_the_visible_neighbour_and_stops_at_the_edge()
    {
        var layout = Panels.Move(Mail, "reader", -1);
        Assert.Equal(["folders", "reader", "messages"], layout.Order);

        Assert.Same(layout, Panels.Move(layout, "folders", -1));
        Assert.False(Panels.CanMove(layout, "folders", -1));
        Assert.True(Panels.CanMove(layout, "folders", +1));

        // With messages hidden, folders moves right past it to swap with the reader.
        var hidden = Panels.Show(Mail, "messages", false);
        var moved = Panels.Move(hidden, "folders", +1);
        Assert.Equal(["reader", "messages", "folders"], moved.Order);
        Assert.Equal(["reader", "folders"], Panels.Visible(moved));
    }

    [Fact]
    public void Widths_are_remembered_and_never_too_narrow()
    {
        var layout = Panels.Resize(Mail, "folders", 300.4);
        Assert.Equal(300, layout.Widths["folders"]);
        Assert.Equal(Panels.MinWidth, Panels.Resize(layout, "messages", 20).Widths["messages"]);
        Assert.Same(layout, Panels.Resize(layout, "nothing", 300));
        Assert.Same(layout, Panels.Resize(layout, "folders", double.NaN));
    }

    [Fact]
    public void A_saved_layout_is_matched_to_the_panes_the_page_has_now()
    {
        var saved = new PanelLayout
        {
            Order = ["gone", "reader", "folders"],
            Hidden = ["gone", "folders"],
            Widths = new Dictionary<string, double> { ["gone"] = 200, ["folders"] = 50 },
        };

        var layout = Panels.Reconcile(saved, "folders", "messages", "reader");

        Assert.Equal(["reader", "folders", "messages"], layout.Order);
        Assert.Equal(["folders"], layout.Hidden);
        Assert.Equal(Panels.MinWidth, layout.Widths["folders"]);
        Assert.DoesNotContain("gone", layout.Widths.Keys);
        Assert.Equal(Mail.Order, Panels.Reconcile(null, "folders", "messages", "reader").Order);
    }

    [Fact]
    public void A_saved_layout_that_hides_everything_shows_everything()
    {
        var saved = new PanelLayout { Order = ["a", "b"], Hidden = ["a", "b"] };
        Assert.Empty(Panels.Reconcile(saved, "a", "b").Hidden);
    }

    [Fact]
    public void Layouts_round_trip_through_json()
    {
        var layout = Panels.Resize(Panels.Show(Mail, "folders", false), "messages", 420);
        var json = JsonSerializer.Serialize(layout, DocumentJson.Options);
        var back = JsonSerializer.Deserialize<PanelLayout>(json, DocumentJson.Options)!;
        Assert.Equal(layout.Order, back.Order);
        Assert.Equal(layout.Hidden, back.Hidden);
        Assert.Equal(420, back.Widths["messages"]);
    }
}
