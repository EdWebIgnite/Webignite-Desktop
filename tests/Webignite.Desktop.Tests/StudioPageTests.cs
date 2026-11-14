using Webignite.Core.Projects;
using Webignite.Core.Studio;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>Studio: every client as a dot on a map, with what is done for them circling.</summary>
public sealed class StudioPageTests
{
    private static (StudioViewModel Page, string ClientId, string ProjectId) Open(TestApp app, bool presenting = false)
    {
        var client = app.Services.Clients.Add("Bosveld Builders");
        var project = app.Services.Projects.Add(new Project { Id = ProjectRules.NewId(), Name = "Bosveld Builders new website", ClientId = client.Id }).Saved!;
        app.Services.Projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "Wireframes" });
        return (new StudioViewModel(app.Services, presenting), client.Id, project.Id);
    }

    [Fact]
    public Task The_map_has_a_dot_for_the_client_and_one_for_its_project() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, clientId, projectId) = Open(app);

        Assert.False(page.IsEmpty);
        Assert.Equal("Bosveld Builders", page.Stars.Single(p => p.Star.Id == clientId).Star.Name);
        Assert.Equal(clientId, page.Stars.Single(p => p.Star.Id == "project:" + projectId).Star.ParentId);
        Assert.Contains("1 client", page.Summary, StringComparison.Ordinal);
        Assert.Contains("1 project under way", page.Summary, StringComparison.Ordinal);
        page.Dispose();
    });

    [Fact]
    public Task Picking_a_client_shows_what_circles_it() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, clientId, projectId) = Open(app);

        page.SelectedId = clientId;

        Assert.True(page.HasPicked);
        Assert.Equal("Client", page.PickedKind);
        Assert.Equal("project:" + projectId, Assert.Single(page.PickedAround).Id);
        Assert.True(page.CanOpenPicked);

        page.PickCommand.Execute(page.PickedAround[0]);
        Assert.Equal("Project", page.PickedKind);
        Assert.Equal("1 task open", page.Picked!.Detail);

        page.ClearPickedCommand.Execute(null);
        Assert.False(page.HasPicked);
        page.Dispose();
    });

    [Fact]
    public Task Demo_names_hide_the_real_ones_and_nothing_opens() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, clientId, _) = Open(app);
        var opened = 0;
        app.Services.Go.OpenClientAction = _ => opened++;

        page.Demo = true;
        page.SelectedId = clientId;

        Assert.DoesNotContain(page.Stars, p => p.Star.Name.Contains("Bosveld", StringComparison.OrdinalIgnoreCase));
        Assert.False(page.CanOpenPicked);
        page.OpenPickedCommand.Execute(null);
        Assert.Equal(0, opened);

        page.Demo = false;
        page.OpenPickedCommand.Execute(null);
        Assert.Equal(1, opened);
        page.Dispose();
    });

    [Fact]
    public Task A_new_project_shows_on_the_map_without_reopening_it() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, clientId, _) = Open(app);

        app.Services.Projects.Add(new Project { Id = ProjectRules.NewId(), Name = "Logo", ClientId = clientId });
        page.Refresh();

        Assert.Equal(2, page.Stars.Count(p => p.Star.Kind == StarKind.Project));
        page.Dispose();
    });

    [Fact]
    public Task The_copy_shown_full_screen_opens_nothing_behind_it() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, clientId, _) = Open(app, presenting: true);

        page.SelectedId = clientId;

        Assert.True(page.Presenting);
        Assert.False(page.CanOpenPicked);
        page.Dispose();
    });

    [Fact]
    public Task With_nothing_to_show_it_says_so() => Ui.Run(() =>
    {
        using var app = new TestApp();
        using var page = new StudioViewModel(app.Services);
        Assert.True(page.IsEmpty);
        Assert.Empty(page.Stars);
    });
}
