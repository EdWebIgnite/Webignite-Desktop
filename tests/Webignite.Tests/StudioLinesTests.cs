using Webignite.Core.Clients;
using Webignite.Core.Projects;
using Webignite.Core.Studio;

namespace Webignite.Tests;

public sealed class StudioLinesTests
{
    private static Heading Stop(string id, string name, double order, string project = "p1") => new() { Id = id, ProjectId = project, Name = name, Order = order };

    private static ProjectTask Job(string id, string? heading, bool done = false, string project = "p1", string? parent = null) =>
        new() { Id = id, ProjectId = project, HeadingId = heading, Title = id, Done = done, ParentId = parent };

    [Fact]
    public void A_project_is_a_line_with_a_stop_for_each_heading_in_order_and_Done_at_the_end()
    {
        var lines = StudioLines.Build(
            [new Project { Id = "p1", Name = "Bosveld Builders new website", ClientId = "c1", Colour = "Blue" }],
            [Stop("h2", "Build", 2), Stop("h1", "Design", 1), Stop("h3", "Launch", 3)],
            [Job("a", "h1", done: true), Job("b", "h1", done: true), Job("c", "h2", done: true), Job("d", "h2"), Job("e", "h3"), Job("sub", "h3", parent: "e")],
            [new Client { Id = "c1", Name = "Bosveld Builders" }]);

        var line = Assert.Single(lines);
        Assert.Equal(["Design", "Build", "Launch", "Done"], line.Stops.Select(s => s.Name));
        Assert.Equal("Bosveld Builders", line.Client);
        Assert.Equal("Blue", line.Colour);
        // Design is finished and Build is half done: halfway between the second and third stops. Subtasks don't count.
        Assert.Equal(1.5, line.Position);
        Assert.Equal("3 of 5 tasks done", line.Detail);
    }

    [Fact]
    public void Tasks_under_no_heading_wait_at_a_Start_stop()
    {
        var line = Assert.Single(StudioLines.Build([new Project { Id = "p1", Name = "Logo" }], [Stop("h1", "Design", 1)],
            [Job("a", null), Job("b", "h1")], []));

        Assert.Equal(["Start", "Design", "Done"], line.Stops.Select(s => s.Name));
        Assert.Equal(0, line.Position);
        Assert.Equal("", line.Client);
    }

    [Fact]
    public void A_project_with_everything_done_is_at_the_last_stop()
    {
        var line = Assert.Single(StudioLines.Build([new Project { Id = "p1", Name = "Logo" }], [Stop("h1", "Design", 1), Stop("h2", "Print", 2)],
            [Job("a", "h1", done: true)], []));

        // Print has nothing under it yet, so it doesn't hold the marker back.
        Assert.Equal(2, line.Position);
        Assert.Equal("Everything done", line.Detail);
    }

    [Fact]
    public void Finished_projects_and_ones_with_no_tasks_have_no_line_and_the_furthest_along_comes_first()
    {
        var lines = StudioLines.Build(
            [
                new Project { Id = "p1", Name = "Alpha" }, new Project { Id = "p2", Name = "Beta" },
                new Project { Id = "p3", Name = "Gone", Status = ProjectStatus.Done }, new Project { Id = "p4", Name = "Empty" },
            ],
            [Stop("h1", "Design", 1), Stop("h2", "Design", 1, "p2"), Stop("h3", "Design", 1, "p3")],
            [Job("a", "h1"), Job("b", "h2", done: true, project: "p2"), Job("c", "h2", project: "p2"), Job("d", "h3", project: "p3")], []);

        Assert.Equal(["Beta", "Alpha"], lines.Select(l => l.Name));
    }

    [Fact]
    public void Demo_names_hide_the_project_and_its_client()
    {
        var line = Assert.Single(StudioLines.Build([new Project { Id = "p1", Name = "Bosveld Builders new website", ClientId = "c1" }], [],
            [Job("a", null)], [new Client { Id = "c1", Name = "Bosveld Builders" }], demo: true));

        Assert.Equal("New website", line.Name);
        Assert.Equal("Marula Coffee Roasters", line.Client);
    }
}
