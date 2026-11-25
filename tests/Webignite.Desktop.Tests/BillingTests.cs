using Webignite.Core.Layout;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>
/// Billing is one entry on the menus. Quotes, Invoices, Money and Recurring are its tabs: each still a page of its
/// own, reached by the tabs across the top of them and by every jump that went to them before.
/// </summary>
public sealed class BillingTests
{
    private static MainViewModel Open(TestApp app)
    {
        app.Services.Settings.Appearance = app.Services.Settings.Appearance with { Menu = MenuKind.Sidebar };
        return new MainViewModel(app.Services);
    }

    private static Module Billing(MainViewModel main) => main.Modules.Single(m => m.Key == Page.Billing);

    [Fact]
    public Task Billing_is_one_entry_and_its_four_pages_are_not_on_the_menus() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = Open(app);

        var onMenus = main.Sidebar.OnMenus().OfType<Module>().Select(m => m.Key).ToList();
        Assert.Contains(Page.Billing, onMenus);
        Assert.DoesNotContain(onMenus, page => page is Page.Quotes or Page.Invoices or Page.Money or Page.Retainers);
        // Nor among what can be put there.
        foreach (var page in new[] { Page.Quotes, Page.Invoices, Page.Money, Page.Retainers })
            Assert.False(main.Sidebar.CanTakeOff(main[page]));
    });

    [Fact]
    public Task Opening_Billing_opens_Invoices_with_the_four_tabs_above_it() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = Open(app);
        Assert.False(main.ShowBillingTabs);

        main.SelectedNav = Billing(main);

        Assert.Equal(Page.Invoices, main.Selected.Key);
        Assert.True(main.ShowBillingTabs);
        Assert.Equal(["Quotes", "Invoices", "Money", "Recurring"], main.BillingTabs.Select(t => t.Title));
        Assert.Equal("Invoices", main.BillingTabs.Single(t => t.IsOn).Title);
        // The sidebar lights Billing, and the Business tab is lit across the top.
        Assert.Same(Billing(main), main.SelectedNav);
        Assert.Equal(AppArea.Business, main.AreaTabs.Single(t => t.IsOn).Area);
        Assert.False(Billing(main).Main.IsBuilt);
    });

    [Fact]
    public Task A_tab_opens_its_page_and_Billing_comes_back_to_the_tab_used_last() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = Open(app);
        main.SelectedNav = Billing(main);

        main.BillingTabs.Single(t => t.Title == "Money").PickCommand.Execute(null);
        Assert.Equal(Page.Money, main.Selected.Key);
        Assert.Equal("Money", main.BillingTabs.Single(t => t.IsOn).Title);
        Assert.Same(Billing(main), main.SelectedNav);

        main.Selected = main[Page.Clients];
        Assert.False(main.ShowBillingTabs);
        Assert.DoesNotContain(main.BillingTabs, t => t.IsOn);

        main.SelectedNav = Billing(main);
        Assert.Equal(Page.Money, main.Selected.Key);
    });

    [Fact]
    public Task A_jump_to_a_quote_or_what_recurs_lands_on_its_tab() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = Open(app);

        app.Services.Go.To(Page.Quotes);
        Assert.Equal(Page.Quotes, main.Selected.Key);
        Assert.Equal("Quotes", main.BillingTabs.Single(t => t.IsOn).Title);
        Assert.True(main.ShowBillingTabs);

        app.Services.Go.To(Page.Retainers);
        Assert.Equal("Recurring", main.BillingTabs.Single(t => t.IsOn).Title);

        // And a jump to Billing itself opens the tab used last.
        app.Services.Go.To(Page.Clients);
        app.Services.Go.To(Page.Billing);
        Assert.Equal(Page.Retainers, main.Selected.Key);
    });

    [Fact]
    public Task On_the_dock_Billing_is_one_button_lit_for_any_of_its_tabs() => Ui.Run(() =>
    {
        using var app = new TestApp();
        // A dock saved before, with the pages that are now tabs on it.
        app.Services.Settings.Appearance = app.Services.Settings.Appearance with { Menu = MenuKind.Dock, Dock = ["Overview", "Invoices", "Quotes", "Clients", "Money"] };
        var main = new MainViewModel(app.Services);

        Assert.Equal(["Overview", "Billing", "Clients"], main.MenuPart.Dock.Select(e => e.PageKey));
        main.Selected = main[Page.Quotes];
        Assert.Equal("Billing", main.MenuPart.Dock.Single(e => e.IsCurrent).PageKey);
    });
}
