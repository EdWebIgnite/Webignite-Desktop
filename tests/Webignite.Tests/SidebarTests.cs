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

public sealed class SidebarOrderTests
{
    private static readonly string[] Defaults =
    [
        "Overview", "Mail",
        "#client-work", "Clients", "Retainers",
        "#billing", "Quotes", "Invoices",
        "#planning", "Todo",
        "#end", "Settings",
    ];

    [Fact]
    public void A_page_moves_past_headings_and_changes_group()
    {
        var layout = Sidebar.Move(new SidebarLayout(), Defaults, "Quotes", -1);
        var order = Sidebar.Resolve(layout, Defaults);
        Assert.Equal(["Overview", "Mail", "#client-work", "Clients", "Retainers", "Quotes", "#billing", "Invoices", "#planning", "Todo", "#end", "Settings"], order);
        Assert.Equal("client-work", Sidebar.GroupOf(order, "Quotes"));
        Assert.Null(Sidebar.GroupOf(order, "Mail"));
        Assert.Equal("billing", Sidebar.GroupOf(order, "Invoices"));
    }

    [Fact]
    public void A_heading_moves_with_its_pages_and_only_past_other_groups()
    {
        var layout = Sidebar.Move(new SidebarLayout(), Defaults, "#billing", -1);
        Assert.Equal(["Overview", "Mail", "#billing", "Quotes", "Invoices", "#client-work", "Clients", "Retainers", "#planning", "Todo", "#end", "Settings"],
            Sidebar.Resolve(layout, Defaults));
        Assert.False(Sidebar.CanMove(layout, Defaults, "#billing", -1));
        Assert.True(Sidebar.CanMove(layout, Defaults, "#planning", -1));
        Assert.False(Sidebar.CanMove(layout, Defaults, "#planning", +1));
    }

    [Fact]
    public void The_bottom_stays_put_and_the_top_page_stops_at_the_top()
    {
        var layout = new SidebarLayout();
        Assert.False(Sidebar.CanMove(layout, Defaults, "Overview", -1));
        Assert.False(Sidebar.CanMove(layout, Defaults, "Todo", +1));
        Assert.False(Sidebar.CanMove(layout, Defaults, "Settings", -1));
        Assert.False(Sidebar.CanMove(layout, Defaults, "#end", -1));
        Assert.Same(layout, Sidebar.Move(layout, Defaults, "Nothing", +1));
    }

    [Fact]
    public void A_page_added_later_slots_in_after_the_one_it_follows_and_a_gone_page_is_dropped()
    {
        var layout = new SidebarLayout { Order = ["Mail", "Overview", "#client-work", "Retainers", "Clients", "Gone", "#billing", "Quotes", "Invoices", "#planning", "Todo", "#end", "Settings"] };
        var defaults = Defaults.ToList();
        defaults.Insert(defaults.IndexOf("Retainers") + 1, "Renewals");
        var order = Sidebar.Resolve(layout, [.. defaults]);
        Assert.Equal(["Mail", "Overview", "#client-work", "Retainers", "Renewals", "Clients", "#billing", "Quotes", "Invoices", "#planning", "Todo", "#end", "Settings"], order);
    }

    [Fact]
    public void Reset_puts_the_order_back_and_opens_every_group_but_keeps_hidden()
    {
        var layout = Sidebar.Fold(Sidebar.Toggle(Sidebar.Move(new SidebarLayout(), Defaults, "Mail", -1)), "billing", open: false);
        var reset = Sidebar.Reset(layout);
        Assert.Empty(reset.Order);
        Assert.Empty(reset.Folded);
        Assert.True(reset.Hidden);
        Assert.Equal(Defaults, Sidebar.Resolve(reset, Defaults));
    }
}
