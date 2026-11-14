using Webignite.Core.Layout;
using Webignite.Core.Projects;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>A page of your own can be for a client, a project, both or neither, and the sidebar lists pages that way.</summary>
public sealed class MyPagesForClientTests
{
    private static List<string> Listed(MainViewModel main) =>
        [.. main.Sidebar.Items.OfType<SubLink>().Where(l => l is MyPageLink or MyPageGroupLink).Select(l => l is MyPageGroupLink ? "[" + l.Title + "]" : l.Title)];

    private static MyPagesViewModel Pages(MainViewModel main)
    {
        main.Selected = main[Page.MyPages];
        return (MyPagesViewModel)main.Current.Page;
    }

    [Fact]
    public Task A_page_for_a_client_is_listed_under_that_client_and_others_stay_loose() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var modic = app.Services.Clients.Add("Modic Digital");
        var main = new MainViewModel(app.Services);
        var pages = Pages(main);
        pages.NewPage();
        pages.EditName = "Monday";
        pages.NewPage();
        pages.EditName = "Forus";

        pages.SetClient(modic.Id);
        pages.Keep();

        Assert.Equal(["Monday", "[Modic Digital]", "Forus", "New page"], Listed(main));
        Assert.Equal("For Modic Digital", pages.ForText);
        Assert.True(pages.HasFor);

        // Taken off the client again, it is loose once more and the heading goes.
        pages.SetClient(null);
        Assert.Equal(["Monday", "Forus", "New page"], Listed(main));
        Assert.False(pages.HasFor);
    });

    [Fact]
    public Task A_page_for_a_clients_project_goes_under_the_client_and_says_both() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var g3 = app.Services.Clients.Add("G3");
        var amos = app.Services.Projects.Add(new Project { Id = ProjectRules.NewId(), Name = "Amos website", ClientId = g3.Id }).Saved!;
        var main = new MainViewModel(app.Services);
        var pages = Pages(main);
        pages.NewPage();
        pages.EditName = "Amos";

        pages.SetProject(amos.Id);
        pages.Keep();

        Assert.Equal("For G3 · Amos website", pages.ForText);
        Assert.Equal(g3.Id, pages.ForClient);
        Assert.Equal(["[G3]", "Amos", "New page"], Listed(main));
    });

    [Fact]
    public Task A_page_for_a_project_with_no_client_is_listed_under_the_project() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var own = app.Services.Projects.Add(new Project { Id = ProjectRules.NewId(), Name = "App fixes" }).Saved!;
        var main = new MainViewModel(app.Services);
        var pages = Pages(main);
        pages.NewPage();
        pages.EditName = "Fixes";

        pages.SetProject(own.Id);
        pages.Keep();

        Assert.Equal(["[App fixes]", "Fixes", "New page"], Listed(main));
        Assert.Equal("For App fixes", pages.ForText);
    });

    [Fact]
    public Task A_click_on_a_clients_heading_opens_the_client() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var modic = app.Services.Clients.Add("Modic Digital");
        var main = new MainViewModel(app.Services);
        var pages = Pages(main);
        pages.NewPage();
        pages.SetClient(modic.Id);
        var opened = new List<string>();
        app.Services.Go.OpenClientAction = client => opened.Add(client.Name);

        main.SelectedNav = main.Sidebar.Items.OfType<MyPageGroupLink>().Single();

        Assert.Equal(["Modic Digital"], opened);
    });

    [Fact]
    public Task Which_client_a_page_is_for_is_kept() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var modic = app.Services.Clients.Add("Modic Digital");
        var pages = new MyPagesViewModel(app.Services);
        pages.NewPage();
        pages.SetClient(modic.Id);
        pages.Dispose();

        Assert.Equal(modic.Id, app.Services.Settings.MyPages.Single().ClientId);
    });
}
