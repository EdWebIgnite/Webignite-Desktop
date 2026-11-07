using Webignite.Core.Projects;

namespace Webignite.Tests;

public class PlannerHomeWidgetsTests
{
    private static readonly HomeSettings Fresh = new();

    [Fact]
    public void Home_starts_with_my_tasks_and_projects_and_keeps_each_widget_once()
    {
        Assert.Equal([HomeWidgetKind.MyTasks, HomeWidgetKind.Projects], PlannerHome.Widgets(Fresh).Select(w => w.Kind));
        var more = PlannerHome.Add(PlannerHome.Add(Fresh, HomeWidgetKind.Notepad), HomeWidgetKind.Notepad);
        Assert.Equal([HomeWidgetKind.MyTasks, HomeWidgetKind.Projects, HomeWidgetKind.Notepad], PlannerHome.Widgets(more).Select(w => w.Kind));
        Assert.False(PlannerHome.Widgets(more)[2].Full);
        var odd = Fresh with { Widgets = [new(HomeWidgetKind.People), new(HomeWidgetKind.People, true), new((HomeWidgetKind)99)] };
        Assert.Equal([HomeWidgetKind.People], PlannerHome.Widgets(odd).Select(w => w.Kind));
        Assert.Empty(PlannerHome.Widgets(PlannerHome.Remove(PlannerHome.Remove(Fresh, HomeWidgetKind.MyTasks), HomeWidgetKind.Projects)));
    }

    [Fact]
    public void Widgets_resize_and_move_before_or_after_another()
    {
        var home = PlannerHome.Add(Fresh, HomeWidgetKind.Milestones);
        home = PlannerHome.Resize(home, HomeWidgetKind.Milestones);
        Assert.True(PlannerHome.Widgets(home).Single(w => w.Kind == HomeWidgetKind.Milestones).Full);
        home = PlannerHome.Move(home, HomeWidgetKind.Milestones, HomeWidgetKind.MyTasks, after: false);
        Assert.Equal([HomeWidgetKind.Milestones, HomeWidgetKind.MyTasks, HomeWidgetKind.Projects], PlannerHome.Widgets(home).Select(w => w.Kind));
        home = PlannerHome.Move(home, HomeWidgetKind.Milestones, HomeWidgetKind.Projects, after: true);
        Assert.Equal([HomeWidgetKind.MyTasks, HomeWidgetKind.Projects, HomeWidgetKind.Milestones], PlannerHome.Widgets(home).Select(w => w.Kind));
        Assert.Equal(PlannerHome.Widgets(home), PlannerHome.Widgets(PlannerHome.Move(home, HomeWidgetKind.Notepad, HomeWidgetKind.MyTasks, true)));
    }

    [Fact]
    public void Status_updates_milestones_and_peoples_plates()
    {
        var at = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
        var today = new DateOnly(2026, 11, 6);
        var site = new Project { Id = "a", Name = "Site", Updates = [new StatusUpdate("u2", ProjectHealth.AtRisk, "Photos late", "me", at), new StatusUpdate("u1", ProjectHealth.OnTrack, "", "me", at.AddDays(-7))] };
        var seo = new Project { Id = "b", Name = "SEO", Updates = [new StatusUpdate("u3", ProjectHealth.OnTrack, "Ranking", "me", at.AddDays(1))] };
        var done = new Project { Id = "c", Name = "Old", Status = ProjectStatus.Done };
        Assert.Equal(["SEO", "Site"], PlannerHome.LatestUpdates([site, seo, done], 5).Select(x => x.Project.Name));
        Assert.Equal("u2", PlannerHome.LatestUpdates([site], 5)[0].Update.Id);

        ProjectTask Task(string id, string project, bool milestone = false, DateOnly? due = null, bool isDone = false, string? who = null) =>
            new() { Id = id, ProjectId = project, Title = id, Milestone = milestone, Due = due, Done = isDone, AssignedTo = who };
        var tasks = new[]
        {
            Task("Launch", "a", milestone: true, due: today.AddDays(9)),
            Task("Sign-off", "b", milestone: true, due: today.AddDays(2)),
            Task("Shipped", "a", milestone: true, isDone: true),
            Task("Archive", "c", milestone: true),
            Task("Copy", "a", due: today.AddDays(-1), who: "me"),
            Task("Photos", "a", who: "me"),
            Task("Logo", "a", who: "jaco", isDone: true),
        };
        Assert.Equal(["Sign-off", "Launch"], PlannerHome.Milestones([site, seo, done], tasks, 5).Select(t => t.Title));
        Assert.Equal((2, 1), PlannerHome.Load([site, seo, done], tasks, "me", today));
        Assert.Equal((0, 0), PlannerHome.Load([site, seo, done], tasks, "jaco", today));
    }

    [Fact]
    public void Peoples_load_counts_what_my_tasks_counts_leaving_out_done_and_deleted_projects()
    {
        var today = new DateOnly(2026, 11, 6);
        var going = new Project { Id = "a", Name = "Site" };
        var finished = new Project { Id = "c", Name = "Old", Status = ProjectStatus.Done };
        var binned = new Project { Id = "d", Name = "Binned", Deleted = new Stamp("me", DateTimeOffset.UnixEpoch) };
        ProjectTask Task(string id, string project, DateOnly? due = null) => new() { Id = id, ProjectId = project, Title = id, Due = due, AssignedTo = "me" };
        var tasks = new[] { Task("Copy", "a", today.AddDays(-1)), Task("Old copy", "c", today.AddDays(-3)), Task("Binned copy", "d", today.AddDays(-2)), Task("Lost", "gone") };
        Assert.Equal((1, 1), PlannerHome.Load([going, finished, binned], tasks, "me", today));
        Assert.Equal(ProjectRules.Mine(tasks, [going, finished, binned], "me").Count, PlannerHome.Load([going, finished, binned], tasks, "me", today).Open);
    }
}

public class PlannerHomeTodosTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Homes_my_tasks_mixes_in_my_to_dos_as_my_tasks_does()
    {
        var tasks = new[]
        {
            new ProjectTask { Id = "t1", ProjectId = "p", Title = "Wireframes", Due = Today.AddDays(2), AssignedTo = "me" },
            new ProjectTask { Id = "t2", ProjectId = "p", Title = "Copy", Due = Today.AddDays(-1), AssignedTo = "me" },
        };
        var todos = new[]
        {
            new Webignite.Core.Tasks.TaskItem { Id = "d1", Title = "Call the bank", Due = Today, Created = Now },
            new Webignite.Core.Tasks.TaskItem { Id = "d2", Title = "Old invoice", Due = Today.AddDays(-3), Created = Now },
            new Webignite.Core.Tasks.TaskItem { Id = "d3", Title = "Done one", Done = true, DoneAt = Now.AddHours(-1), Created = Now.AddDays(-5) },
        };
        Assert.Equal(["Call the bank", "Wireframes"], PlannerHome.Upcoming(tasks, todos, Today).Select(i => i.Title));
        Assert.Equal(["Old invoice", "Copy"], PlannerHome.Overdue(tasks, todos, Today).Select(i => i.Title));
        Assert.Equal(["Done one"], PlannerHome.Completed(tasks, todos, "me", Now.AddDays(-14)).Select(i => i.Title));
    }
}
