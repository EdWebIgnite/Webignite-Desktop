using System.Security.Cryptography;
using Webignite.Core.Projects;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>Projects groundwork: you and your partner, and every change recorded with who made it and when.</summary>
public sealed class ProjectTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-projects-").FullName;
    private readonly LocalStore store;
    private readonly Clock clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.FromHours(2)));
    private string who = "eddie";
    private readonly ProjectService projects;

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

    public ProjectTests()
    {
        store = new LocalStore(Path.Combine(folder, "webignite.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        projects = new ProjectService(store, () => who, clock);
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private Project NewProject(string name = "Bosveld Builders website") => projects.Add(new Project { Id = ProjectRules.NewId(), Name = name }).Saved!;

    private ProjectTask NewTask(string projectId, string title, string? heading = null, string? parent = null) =>
        projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = projectId, Title = title, HeadingId = heading, ParentId = parent }).Saved!;

    [Fact]
    public void You_and_your_partner_are_made_once_and_can_be_renamed()
    {
        var team = ProjectRules.Ensure(new TeamSettings(), null);
        Assert.Equal("Me", team.Me!.Name);
        Assert.Equal("Partner", team.Partner!.Name);
        Assert.NotEqual(team.Me.Id, team.Partner.Id);
        Assert.Same(team.Me, ProjectRules.Ensure(team, "Someone else").Me);

        Assert.Empty(ProjectRules.Check(team.Me with { Name = "Eddie" }, team.Partner with { Name = "Jaco", Email = "jaco@webignite.co.za" }));
        Assert.Single(ProjectRules.Check(team.Me with { Name = "Eddie" }, team.Partner with { Name = " eddie " }));
        Assert.Single(ProjectRules.Check(team.Me with { Name = "Eddie" }, team.Partner with { Name = "Jaco", Email = "jaco" }));
        Assert.Equal(2, ProjectRules.Check(team.Me with { Name = "" }, team.Partner with { Name = " " }).Count);
    }

    [Fact]
    public void A_new_project_records_who_made_it_and_when_for_every_field()
    {
        var project = NewProject();
        var stored = store.GetProject(project.Id)!;

        Assert.Equal("eddie", stored.Created.By);
        Assert.Equal(clock.GetUtcNow(), stored.Created.At);
        Assert.Equal(stored.Created, stored.Changed);
        Assert.All(stored.Fields.Values, s => Assert.Equal(stored.Created, s));
        Assert.Contains("name", stored.Fields.Keys);
        Assert.Null(stored.Deleted);
    }

    [Fact]
    public void A_change_stamps_only_the_fields_that_changed()
    {
        var project = NewProject();
        var made = project.Created;
        clock.Now = clock.Now.AddHours(1);
        who = "jaco";

        var changed = projects.Change(project.Id, p => p with { Status = ProjectStatus.OnHold, Name = "Bosveld Builders website " }).Saved!;

        Assert.Equal("Bosveld Builders website", changed.Name);
        Assert.Equal(made, changed.Created);
        Assert.Equal(new Stamp("jaco", clock.GetUtcNow()), changed.Changed);
        Assert.Equal(new Stamp("jaco", clock.GetUtcNow()), changed.Fields["status"]);
        Assert.Equal(made, changed.Fields["name"]); // trimmed back to what it was, so not a change
        Assert.Equal(made, changed.Fields["due"]);

        clock.Now = clock.Now.AddHours(1);
        var same = projects.Change(project.Id, p => p).Saved!;
        Assert.Equal(changed.Changed, same.Changed);
    }

    [Fact]
    public void Projects_and_tasks_need_names_and_subtasks_go_one_level_deep()
    {
        Assert.Single(projects.Add(new Project { Id = ProjectRules.NewId(), Name = " " }).Problems);
        var project = NewProject();
        Assert.Single(projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "" }).Problems);

        var task = NewTask(project.Id, "Home page");
        var sub = NewTask(project.Id, "Hero photo", parent: task.Id);
        Assert.Equal(task.Id, sub.ParentId);
        Assert.Contains("one level", Assert.Single(projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "Crop it", ParentId = sub.Id }).Problems));
        Assert.Single(projects.ChangeTask(task.Id, t => t with { ParentId = NewTask(project.Id, "Other").Id }).Problems);
        Assert.Single(projects.ChangeTask(task.Id, t => t with { Start = new DateOnly(2026, 11, 10), Due = new DateOnly(2026, 11, 9) }).Problems);
    }

    [Fact]
    public void New_tasks_go_at_the_end_of_their_heading_and_moving_a_task_takes_its_subtasks_along()
    {
        var project = NewProject();
        var design = projects.AddHeading(project.Id, "Design").Saved!;
        var build = projects.AddHeading(project.Id, "Build").Saved!;
        Assert.Equal(["Design", "Build"], projects.Headings(project.Id).Select(h => h.Name));

        var first = NewTask(project.Id, "Wireframes", design.Id);
        var second = NewTask(project.Id, "Colours", design.Id);
        var elsewhere = NewTask(project.Id, "Hosting", build.Id);
        Assert.True(second.Order > first.Order);
        Assert.Equal(1, elsewhere.Order);
        var sub = NewTask(project.Id, "Mobile wireframe", parent: first.Id, heading: design.Id);

        projects.ChangeTask(first.Id, t => t with { HeadingId = build.Id });
        Assert.Equal(build.Id, projects.GetTask(sub.Id)!.HeadingId);
        Assert.Equal(1.5, ProjectRules.Between(1, 2));
        Assert.Equal(0, ProjectRules.Between(null, 1));
    }

    [Fact]
    public void Deleting_leaves_a_marker_with_who_and_when_and_undo_puts_it_all_back()
    {
        var project = NewProject();
        var design = projects.AddHeading(project.Id, "Design").Saved!;
        var task = NewTask(project.Id, "Wireframes", design.Id);
        NewTask(project.Id, "Mobile", design.Id, task.Id);
        clock.Now = clock.Now.AddMinutes(5);

        var gone = projects.Delete(project.Id)!;
        Assert.Empty(projects.Projects());
        Assert.Empty(projects.AllTasks(project.Id));
        var marker = store.GetProject(project.Id)!;
        Assert.Equal(new Stamp("eddie", clock.GetUtcNow()), marker.Deleted);
        Assert.All(store.ListProjectTasks(), t => Assert.NotNull(t.Deleted));

        clock.Now = clock.Now.AddMinutes(1);
        projects.Restore(gone);
        Assert.Single(projects.Projects());
        Assert.Equal(2, projects.AllTasks(project.Id).Count);
        Assert.Equal(["Design"], projects.Headings(project.Id).Select(h => h.Name));
        Assert.Equal(clock.GetUtcNow(), store.GetProject(project.Id)!.Fields["deleted"].At);
    }

    [Fact]
    public void Deleting_a_heading_keeps_its_tasks_under_no_heading_until_undone()
    {
        var project = NewProject();
        var design = projects.AddHeading(project.Id, "Design").Saved!;
        var task = NewTask(project.Id, "Wireframes", design.Id);

        var gone = projects.DeleteHeading(design.Id)!;
        Assert.Empty(projects.Headings(project.Id));
        Assert.Null(projects.GetTask(task.Id)!.HeadingId);

        projects.Restore(gone);
        Assert.Equal(design.Id, projects.GetTask(task.Id)!.HeadingId);
    }

    [Fact]
    public void Changes_reach_pages_with_the_projects_id()
    {
        var seen = new List<string?>();
        projects.Changed += seen.Add;
        var project = NewProject();
        NewTask(project.Id, "Wireframes");
        Assert.Equal([null, project.Id], seen);
    }
}
