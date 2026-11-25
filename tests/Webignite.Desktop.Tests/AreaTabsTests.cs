using Webignite.Core.Layout;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>
/// The app in five areas: tabs across the top, a page of tiles for Business, Computer and Tools, and the sidebar as
/// the shortlist of what is pinned.
/// </summary>
public sealed class AreaTabsTests
{
    private static List<Page> OnSidebar(MainViewModel main) => [.. main.Sidebar.OnMenus().OfType<Module>().Where(m => !m.InPlanner).Select(m => m.Key)];

    [Fact]
    public Task There_are_five_tabs_and_the_sidebar_is_a_shortlist() => Ui.Run(() =>
    {
        using var app = new TestApp();
        app.Services.Settings.Appearance = app.Services.Settings.Appearance with { Menu = MenuKind.Sidebar };
        var main = new MainViewModel(app.Services);

        Assert.Equal(["Mail", "Business", "Planner", "Computer", "Tools"], main.AreaTabs.Select(t => t.Title));
        Assert.Equal([Page.Overview, Page.Mail, Page.Clients, Page.Quotes, Page.Invoices, Page.Files, Page.Notes, Page.Settings], OnSidebar(main));
        Assert.Contains(main.Sidebar.OnMenus(), item => item is SectionLink);
        Assert.Empty(main.Sidebar.OnMenus().OfType<ToolLink>());
    });

    [Fact]
    public Task A_tab_opens_its_area_and_is_lit_while_a_page_of_that_area_is_open() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = new MainViewModel(app.Services);

        main.AreaTabs.Single(t => t.Area == AppArea.Tools).PickCommand.Execute(null);
        Assert.Equal(Page.Tools, main.Selected.Key);
        Assert.Equal(AppArea.Tools, main.AreaTabs.Single(t => t.IsOn).Area);

        main.AreaTabs.Single(t => t.Area == AppArea.Planner).PickCommand.Execute(null);
        Assert.Equal(Page.Home, main.Selected.Key);
        Assert.True(main.Sidebar.IsPlanner);
        main.Selected = main[Page.Calendar];
        Assert.Equal(AppArea.Planner, main.AreaTabs.Single(t => t.IsOn).Area);

        main.Selected = main[Page.Invoices];
        Assert.Equal(AppArea.Business, main.AreaTabs.Single(t => t.IsOn).Area);
        Assert.False(main.Sidebar.IsPlanner);

        main.Selected = main[Page.Settings];
        Assert.DoesNotContain(main.AreaTabs, t => t.IsOn);
    });

    [Fact]
    public Task The_areas_own_pages_are_never_on_the_menus() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = new MainViewModel(app.Services);

        foreach (var page in new[] { Page.Business, Page.Computer, Page.Tools })
        {
            Assert.True(main[page].IsArea);
            Assert.False(main.Sidebar.IsOnMenus(SidebarViewModel.KeyOf(page)));
            Assert.False(main.Sidebar.CanTakeOff(main[page]));
            main.Sidebar.ShowOnMenus(SidebarViewModel.KeyOf(page), true);
            Assert.DoesNotContain(main[page], main.Sidebar.OnMenus());
        }
    });

    [Fact]
    public Task Pinning_a_tile_puts_it_on_the_sidebar_and_it_is_still_there_next_time() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = new MainViewModel(app.Services);
        main.Selected = main[Page.Computer];
        var computer = (AreaViewModel)main.Current.Page;
        var calculator = computer.Groups.SelectMany(g => g.Tiles).Single(t => t.Title == "Calculator");
        Assert.False(calculator.IsPinned);

        calculator.PinCommand.Execute(null);
        Assert.True(calculator.IsPinned);
        Assert.Equal(ToolKind.Calculator, main.Sidebar.OnMenus().OfType<ToolLink>().Single().Kind);

        // Taken off again, it goes back to living on its area's page only.
        calculator.PinCommand.Execute(null);
        Assert.False(calculator.IsPinned);
        Assert.Empty(main.Sidebar.OnMenus().OfType<ToolLink>());

        calculator.PinCommand.Execute(null);
        var again = new MainViewModel(app.Services);
        Assert.Equal(ToolKind.Calculator, again.Sidebar.OnMenus().OfType<ToolLink>().Single().Kind);
    });

    [Fact]
    public Task A_page_the_person_makes_goes_on_the_sidebar() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var main = new MainViewModel(app.Services);
        Assert.DoesNotContain(Page.MyPages, OnSidebar(main));

        main.Selected = main[Page.MyPages];
        var pages = (MyPagesViewModel)main.Current.Page;
        pages.NewPage();
        pages.EditName = "Monday";
        pages.Keep();

        Assert.Contains(Page.MyPages, OnSidebar(main));
        Assert.Contains(main.Sidebar.Items.OfType<MyPageLink>(), link => link.Title == "Monday");
    });

    [Fact]
    public Task Someone_who_already_has_pages_of_their_own_keeps_them_on_the_sidebar() => Ui.Run(() =>
    {
        using var app = new TestApp();
        app.Services.Settings.MyPages = [new MyPage { Id = "p1", Name = "Monday" }];
        var main = new MainViewModel(app.Services);

        Assert.Contains(Page.MyPages, OnSidebar(main));
        Assert.Contains(main.Sidebar.Items.OfType<MyPageLink>(), link => link.Title == "Monday");
    });

    [Fact]
    public Task A_new_install_with_the_dock_has_everything_the_dock_starts_with() => Ui.Run(() =>
    {
        using var app = new TestApp();
        Assert.Equal(MenuKind.Dock, app.Services.Settings.Appearance.Menu);
        var main = new MainViewModel(app.Services);

        Assert.Equal(DockPages.Usual, main.MenuPart.Dock.Select(e => e.PageKey));
        Assert.Equal([Page.Overview, Page.Mail, Page.Clients, Page.Quotes, Page.Invoices, Page.Money, Page.Files, Page.Notes, Page.Settings], OnSidebar(main));
    });

    [Fact]
    public Task Someone_who_uses_the_dock_keeps_what_is_on_it() => Ui.Run(() =>
    {
        using var app = new TestApp();
        app.Services.Settings.Appearance = app.Services.Settings.Appearance with { Menu = MenuKind.Dock, Dock = ["Overview", "Money", DockKeys.Tool("Calculator")] };
        var main = new MainViewModel(app.Services);

        Assert.Contains(Page.Money, OnSidebar(main));
        Assert.Equal(ToolKind.Calculator, main.Sidebar.OnMenus().OfType<ToolLink>().Single().Kind);
        Assert.DoesNotContain(Page.Contacts, OnSidebar(main));

        main.Selected = main[Page.Business];
        Assert.Equal("5 of these are pinned to the menu.", ((AreaViewModel)main.Current.Page).Summary);
    });
}
