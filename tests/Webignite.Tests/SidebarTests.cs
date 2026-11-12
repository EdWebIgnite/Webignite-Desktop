using System.Text.Json;
using Webignite.Core.Layout;

namespace Webignite.Tests;

public sealed class SidebarTests
{
    [Fact]
    public void The_button_goes_from_open_to_icons_to_hidden_and_back()
    {
        var layout = new SidebarLayout();
        Assert.Equal(SidebarMode.Open, layout.Mode);
        layout = Sidebar.Toggle(layout);
        Assert.Equal(SidebarMode.Icons, layout.Mode);
        layout = Sidebar.Toggle(layout);
        Assert.Equal(SidebarMode.Hidden, layout.Mode);
        Assert.False(layout.Icons);
        Assert.Equal(SidebarMode.Open, Sidebar.Toggle(layout).Mode);
    }

    [Fact]
    public void A_layout_saved_before_icons_existed_still_reads_as_hidden()
    {
        var old = JsonSerializer.Deserialize<SidebarLayout>("""{"hidden":true}""", DocumentJson.Options)!;
        Assert.Equal(SidebarMode.Hidden, old.Mode);
        Assert.Equal(SidebarMode.Icons, Sidebar.Show(old, SidebarMode.Icons).Mode);
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
    public void A_new_group_starts_a_moved_order_afresh_but_keeps_the_rest()
    {
        var saved = new SidebarLayout { Icons = true, Folded = ["billing"], Order = ["Overview", "#billing", "Quotes", "#end", "Settings"] };
        var known = Sidebar.Reconcile(saved, "billing");
        Assert.Equal(saved.Order, known.Order);
        var added = Sidebar.Reconcile(saved, "billing", "computer");
        Assert.Empty(added.Order);
        Assert.True(added.Icons);
        Assert.Equal(["billing"], added.Folded);
    }

    [Fact]
    public void Layouts_round_trip_through_json()
    {
        var layout = Sidebar.Fold(Sidebar.Show(new SidebarLayout(), SidebarMode.Hidden), "planning", open: false);
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
    public void A_dragged_page_lands_above_or_below_where_it_is_dropped()
    {
        var layout = Sidebar.MoveTo(new SidebarLayout(), Defaults, "Todo", "Mail", below: false);
        Assert.Equal(["Overview", "Todo", "Mail", "#client-work", "Clients", "Retainers", "#billing", "Quotes", "Invoices", "#planning", "#end", "Settings"],
            Sidebar.Resolve(layout, Defaults));
        layout = Sidebar.MoveTo(new SidebarLayout(), Defaults, "Overview", "#billing", below: true);
        Assert.Equal("billing", Sidebar.GroupOf(Sidebar.Resolve(layout, Defaults), "Overview"));
        Assert.Equal("Quotes", Sidebar.Resolve(layout, Defaults)[Sidebar.Resolve(layout, Defaults).IndexOf("Overview") + 1]);
        // Above the bottom heading is the end of the last group; past it is not allowed.
        layout = Sidebar.MoveTo(new SidebarLayout(), Defaults, "Mail", "#end", below: false);
        Assert.Equal("planning", Sidebar.GroupOf(Sidebar.Resolve(layout, Defaults), "Mail"));
        Assert.False(Sidebar.CanMoveTo(new SidebarLayout(), Defaults, "Mail", "#end", below: true));
        Assert.False(Sidebar.CanMoveTo(new SidebarLayout(), Defaults, "Mail", "Settings", below: false));
        Assert.False(Sidebar.CanMoveTo(new SidebarLayout(), Defaults, "Mail", "Overview", below: true));
    }

    [Fact]
    public void A_dragged_heading_takes_its_pages_before_or_after_another_group()
    {
        var layout = Sidebar.MoveTo(new SidebarLayout(), Defaults, "#planning", "#client-work", below: false);
        Assert.Equal(["Overview", "Mail", "#planning", "Todo", "#client-work", "Clients", "Retainers", "#billing", "Quotes", "Invoices", "#end", "Settings"],
            Sidebar.Resolve(layout, Defaults));
        layout = Sidebar.MoveTo(new SidebarLayout(), Defaults, "#client-work", "Quotes", below: false);
        Assert.Equal(["Overview", "Mail", "#billing", "Quotes", "Invoices", "#client-work", "Clients", "Retainers", "#planning", "Todo", "#end", "Settings"],
            Sidebar.Resolve(layout, Defaults));
        layout = Sidebar.MoveTo(new SidebarLayout(), Defaults, "#client-work", "#end", below: false);
        Assert.Equal(["Overview", "Mail", "#billing", "Quotes", "Invoices", "#planning", "Todo", "#client-work", "Clients", "Retainers", "#end", "Settings"],
            Sidebar.Resolve(layout, Defaults));
        Assert.False(Sidebar.CanMoveTo(new SidebarLayout(), Defaults, "#billing", "Overview", below: false));
        Assert.False(Sidebar.CanMoveTo(new SidebarLayout(), Defaults, "#billing", "Invoices", below: true));
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
        var layout = Sidebar.Fold(Sidebar.Show(Sidebar.Move(new SidebarLayout(), Defaults, "Mail", -1), SidebarMode.Hidden), "billing", open: false);
        var reset = Sidebar.Reset(layout);
        Assert.Empty(reset.Order);
        Assert.Empty(reset.Folded);
        Assert.True(reset.Hidden);
        Assert.Equal(Defaults, Sidebar.Resolve(reset, Defaults));
    }

    private static readonly string[] Menu = ["Overview", "#work", "Clients", "Retainers", "#tools", "tool-Calculator", "tool-Qr", "#end", "Settings"];

    [Fact]
    public void An_entry_taken_off_is_gone_from_the_menus_and_keeps_its_place_for_when_it_comes_back()
    {
        var layout = Sidebar.TakeOff(new SidebarLayout(), "Retainers");
        Assert.Equal(["Overview", "#work", "Clients", "#tools", "tool-Calculator", "tool-Qr", "#end", "Settings"], Sidebar.Shown(layout, Menu));
        Assert.Equal(["Retainers"], Sidebar.TakenOff(layout, Menu));
        Assert.Equal(Menu, Sidebar.Shown(Sidebar.PutBack(layout, "Retainers"), Menu));
        // Taking it off twice, or putting back what isn't off, changes nothing.
        Assert.Same(layout, Sidebar.TakeOff(layout, "Retainers"));
        Assert.Same(layout, Sidebar.PutBack(layout, "Clients"));
    }

    [Fact]
    public void A_group_with_nothing_left_on_it_goes_too_but_the_space_above_settings_stays()
    {
        var layout = Sidebar.TakeOff(Sidebar.TakeOff(new SidebarLayout(), "tool-Calculator"), "tool-Qr");
        Assert.Equal(["Overview", "#work", "Clients", "Retainers", "#end", "Settings"], Sidebar.Shown(layout, Menu));
        Assert.Equal(["tool-Calculator", "tool-Qr"], Sidebar.TakenOff(layout, Menu));
        Assert.Equal(Menu, Sidebar.Shown(Sidebar.PutAllBack(layout), Menu));
    }

    [Fact]
    public void Settings_planners_home_and_headings_always_stay()
    {
        Assert.False(Sidebar.CanTakeOff("Settings"));
        Assert.False(Sidebar.CanTakeOff("Home"));
        Assert.False(Sidebar.CanTakeOff("#work"));
        Assert.True(Sidebar.CanTakeOff("Clients"));
        Assert.True(Sidebar.CanTakeOff("tool-Qr"));
        var layout = new SidebarLayout();
        Assert.Same(layout, Sidebar.TakeOff(layout, "Settings"));
    }

    [Fact]
    public void What_is_off_stays_off_when_the_order_is_changed_or_put_back()
    {
        var layout = Sidebar.TakeOff(new SidebarLayout(), "Retainers");
        layout = Sidebar.Move(layout, Menu, "tool-Qr", -1);
        Assert.Equal(["Overview", "#work", "Clients", "#tools", "tool-Qr", "tool-Calculator", "#end", "Settings"], Sidebar.Shown(layout, Menu));
        Assert.True(Sidebar.Reset(layout).IsOff("Retainers"));
        // A layout saved before this existed has nothing off.
        Assert.Empty(JsonSerializer.Deserialize<SidebarLayout>("""{"hidden":false}""", DocumentJson.Options)!.Off);
    }
}
