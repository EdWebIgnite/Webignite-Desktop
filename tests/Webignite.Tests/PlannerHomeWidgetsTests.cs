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
        Assert.Equal((2, 1), PlannerHome.Load(tasks, "me", today));
        Assert.Equal((0, 0), PlannerHome.Load(tasks, "jaco", today));
    }
}
