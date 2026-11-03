using System.Text.Json;
using Webignite.Core.Layout;

namespace Webignite.Tests;

public sealed class SidebarTests
{
    [Fact]
    public void The_sidebar_hides_and_shows()
    {
        var layout = new SidebarLayout();
        Assert.False(layout.Hidden);
        layout = Sidebar.Toggle(layout);
        Assert.True(layout.Hidden);
        Assert.False(Sidebar.Toggle(layout).Hidden);
    }

    [Fact]
    public void Groups_fold_up_and_open_out_once_each()
    {
        var layout = Sidebar.Fold(new SidebarLayout(), "billing", open: false);
        Assert.False(layout.IsOpen("billing"));
        Assert.True(layout.IsOpen("planning"));

        Assert.Same(layout, Sidebar.Fold(layout, "billing", open: false));
        Assert.Equal(["billing"], layout.Folded);

        var opened = Sidebar.Fold(layout, "billing", open: true);
        Assert.True(opened.IsOpen("billing"));
        Assert.Same(opened, Sidebar.Fold(opened, "billing", open: true));
    }

    [Fact]
    public void A_saved_layout_forgets_groups_that_are_gone()
    {
        var saved = new SidebarLayout { Hidden = true, Folded = ["billing", "gone", "billing"] };
        var layout = Sidebar.Reconcile(saved, "client-work", "billing", "planning");
        Assert.True(layout.Hidden);
        Assert.Equal(["billing"], layout.Folded);
        Assert.False(Sidebar.Reconcile(null, "billing").Hidden);
    }

    [Fact]
    public void Layouts_round_trip_through_json()
    {
        var layout = Sidebar.Fold(Sidebar.Toggle(new SidebarLayout()), "planning", open: false);
        var json = JsonSerializer.Serialize(layout, DocumentJson.Options);
        var back = JsonSerializer.Deserialize<SidebarLayout>(json, DocumentJson.Options)!;
        Assert.True(back.Hidden);
        Assert.Equal(["planning"], back.Folded);
    }
}
