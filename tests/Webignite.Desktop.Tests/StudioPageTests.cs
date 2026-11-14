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
    public Task The_demo_studio_replaces_the_real_one_and_nothing_opens() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, clientId, _) = Open(app);
        var opened = 0;
        app.Services.Go.OpenClientAction = _ => opened++;

        page.Demo = true;

        Assert.Equal(27, page.Stars.Count(p => p.Star.ParentId is null));
        Assert.DoesNotContain(page.Stars, p => p.Star.Name.Contains("Bosveld", StringComparison.OrdinalIgnoreCase));
        Assert.True(page.Lines.Count >= 8);
        page.SelectedId = page.Stars[0].Star.Id;
        Assert.False(page.CanOpenPicked);
        Assert.False(page.CanHidePicked);
        page.OpenPickedCommand.Execute(null);
        Assert.Equal(0, opened);

        page.Demo = false;
        page.SelectedId = clientId;
        page.OpenPickedCommand.Execute(null);
        Assert.Equal(1, opened);
        page.Dispose();
    });

    [Fact]
    public Task A_contact_that_isnt_a_client_can_be_left_off_and_brought_back() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, _, _) = Open(app);
        var payfast = app.Services.Clients.Add("PayFast Fees");
        page.Refresh();
        Assert.Contains(page.Stars, p => p.Star.Id == payfast.Id);

        page.SelectedId = payfast.Id;
        Assert.True(page.CanHidePicked);
        page.HidePickedCommand.Execute(null);

        Assert.DoesNotContain(page.Stars, p => p.Star.Id == payfast.Id);
        Assert.True(page.HasHidden);
        Assert.Equal("1 left off. Show it again", page.HiddenText);
        // It is remembered: another copy of the page leaves it off too, and it is still in Clients.
        using (var again = new StudioViewModel(app.Services))
            Assert.DoesNotContain(again.Stars, p => p.Star.Id == payfast.Id);
        Assert.NotNull(app.Services.Clients.Get(payfast.Id));

        page.ShowHiddenCommand.Execute(null);
        Assert.Contains(page.Stars, p => p.Star.Id == payfast.Id);
        Assert.False(page.HasHidden);
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
    public Task Lines_shows_each_running_project_with_a_stop_per_heading() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, _, projectId) = Open(app);
        var design = app.Services.Projects.AddHeading(projectId, "Design").Saved!;
        app.Services.Projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = projectId, HeadingId = design.Id, Title = "Colours", Done = true });
        page.Refresh();

        page.ShowLinesCommand.Execute(null);

        Assert.True(page.IsLines);
        Assert.False(page.IsMap);
        var line = Assert.Single(page.Lines);
        Assert.Equal(["Start", "Design", "Done"], line.Stops.Select(s => s.Name));
        Assert.Equal("Bosveld Builders", line.Client);
        Assert.False(page.NoLines);
        page.Dispose();
    });

    [Fact]
    public Task A_click_on_a_line_opens_its_project_but_not_in_the_demo_studio() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, _, projectId) = Open(app);
        var opened = new List<string>();
        app.Services.Go.OpenProjectAction = opened.Add;

        page.Open(page.Lines[0]);
        page.Demo = true;
        page.Open(page.Lines[0]);

        Assert.Equal([projectId], opened);
        page.Dispose();
    });

    [Fact]
    public Task The_wall_has_a_card_for_each_client_and_a_click_opens_it() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, clientId, projectId) = Open(app);
        var opened = new List<string>();
        app.Services.Go.OpenClientAction = c => opened.Add(c.Id);

        page.ShowWallCommand.Execute(null);

        Assert.True(page.IsWall);
        var card = Assert.Single(page.Wall);
        Assert.Equal("BB", card.Initials);
        Assert.Equal("Work under way", card.Status);
        Assert.Equal("project:" + projectId, Assert.Single(card.Busy).Id);
        page.Open(card);
        Assert.Equal([clientId], opened);
        page.Dispose();
    });

    [Fact]
    public Task While_presenting_Esc_first_lets_go_of_a_picked_client_and_then_closes() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, clientId, _) = Open(app, presenting: true);
        page.SelectedId = clientId;

        Assert.True(Webignite.Desktop.Views.StudioView.StepBack(page));
        Assert.Null(page.SelectedId);
        // Nothing left to let go of: the window closes.
        Assert.False(Webignite.Desktop.Views.StudioView.StepBack(page));
        page.Dispose();
    });

    [Fact]
    public Task The_picture_can_be_changed_while_presenting() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, _, _) = Open(app, presenting: true);

        page.ShowWallCommand.Execute(null);
        Assert.True(page.IsWall);
        page.ShowLinesCommand.Execute(null);
        Assert.True(page.IsLines);
        page.Dispose();
    });

    [Fact]
    public Task A_planet_dragged_to_a_place_stays_there_and_reset_puts_it_back() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, clientId, projectId) = Open(app);
        var before = page.Stars.Single(p => p.Star.Id == clientId);
        var moonBefore = page.Stars.Single(p => p.Star.Id == "project:" + projectId);
        Assert.False(page.HasMoved);

        page.Move(clientId, 0.5, -0.4);

        var after = page.Stars.Single(p => p.Star.Id == clientId);
        Assert.Equal((0.5, -0.4), (after.X, after.Y));
        var moon = page.Stars.Single(p => p.Star.Id == "project:" + projectId);
        Assert.Equal(moonBefore.X - before.X, moon.X - after.X, 6);
        Assert.True(page.HasMoved);
        // It is remembered: another copy of the page has it in the same place.
        using (var again = new StudioViewModel(app.Services))
            Assert.Equal(0.5, again.Stars.Single(p => p.Star.Id == clientId).X);

        page.ResetLayoutCommand.Execute(null);
        Assert.Equal(before, page.Stars.Single(p => p.Star.Id == clientId));
        Assert.False(page.HasMoved);
        page.Dispose();
    });

    [Fact]
    public Task The_demo_studios_planets_are_placed_apart_from_your_own() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, clientId, _) = Open(app);
        page.Move(clientId, 0.5, 0.5);

        page.Demo = true;
        var demo = page.Stars.First(p => p.Star.ParentId is null);
        page.Move(demo.Star.Id, -0.6, 0.2);
        page.ResetLayoutCommand.Execute(null);

        // Resetting the demo studio's layout leaves your own client where you put it.
        page.Demo = false;
        Assert.Equal(0.5, page.Stars.Single(p => p.Star.Id == clientId).X);
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
