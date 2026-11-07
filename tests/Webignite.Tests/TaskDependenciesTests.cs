using Webignite.Core.Projects;

namespace Webignite.Tests;

public class TaskDependenciesTests
{
    private static ProjectTask Task(string id, double order = 0, bool done = false, string project = "p", params string[] blockedBy) =>
        new() { Id = id, ProjectId = project, Title = id, Order = order, Done = done, BlockedBy = blockedBy };

    [Fact]
    public void A_task_waits_on_others_and_the_others_show_what_they_hold_up()
    {
        var wireframes = Task("Wireframes", 1, done: true);
        var copy = Task("Copy", 2);
        var build = Task("Build", 3, blockedBy: ["Wireframes", "Copy", "Gone"]);
        var tasks = new[] { wireframes, copy, build };
        Assert.Equal(["Wireframes", "Copy"], TaskDependencies.BlockedBy(build, tasks).Select(t => t.Id));
        Assert.Equal(["Build"], TaskDependencies.Blocking(copy, tasks).Select(t => t.Id));
        Assert.True(TaskDependencies.IsWaiting(build, tasks));
        Assert.Equal("Copy", TaskDependencies.WaitingOn(build, tasks));
        Assert.False(TaskDependencies.IsWaiting(build, [wireframes, copy with { Done = true }, build]));
    }

    [Fact]
    public void A_task_cant_wait_on_itself_twice_on_one_or_round_in_a_circle()
    {
        var a = Task("A", 1);
        var b = Task("B", 2, blockedBy: ["A"]);
        var c = Task("C", 3, blockedBy: ["B"]);
        var other = Task("Elsewhere", 4, project: "q");
        var tasks = new[] { a, b, c, other };
        Assert.Contains("itself", TaskDependencies.Problem(a, "A", tasks));
        Assert.Contains("already waiting on that", TaskDependencies.Problem(b, "A", tasks));
        Assert.Contains("this one can't", TaskDependencies.Problem(a, "C", tasks));
        Assert.Contains("isn't in this project", TaskDependencies.Problem(a, "Elsewhere", tasks));
        Assert.Equal("", TaskDependencies.Problem(c, "A", tasks));
        Assert.Equal(["A"], TaskDependencies.Choices(c, tasks).Select(t => t.Id));
        Assert.Empty(TaskDependencies.Choices(a, tasks));
    }

    [Fact]
    public void Adding_and_taking_off_keeps_each_once()
    {
        var task = TaskDependencies.Add(TaskDependencies.Add(Task("Build"), "Copy"), "Copy");
        Assert.Equal(["Copy"], task.BlockedBy);
        Assert.Empty(TaskDependencies.Remove(task, "Copy").BlockedBy);
    }

    [Fact]
    public void A_task_leaving_for_another_project_is_unhooked_both_ways()
    {
        var brief = Task("Brief", 1);
        var build = Task("Build", 2, blockedBy: ["Brief"]);
        var pages = Task("Pages", 1, blockedBy: ["Brief"]) with { ParentId = "Build" };
        var launch = Task("Launch", 3, blockedBy: ["Pages", "Brief", "Build"]);
        var tasks = new[] { brief, build, pages, launch };

        var going = TaskDependencies.Going(build, tasks);
        Assert.Equal(["Build", "Pages"], going.Order());
        Assert.Empty(TaskDependencies.Unhooked(build, going).BlockedBy);
        Assert.Empty(TaskDependencies.Unhooked(pages, going).BlockedBy);
        Assert.Equal(["Brief"], TaskDependencies.Unhooked(launch, going).BlockedBy);
        Assert.Same(brief, TaskDependencies.Unhooked(brief, going));

        // A subtask moved on its own goes alone, and its task stays as it is.
        var alone = TaskDependencies.Going(pages, tasks);
        Assert.Equal(["Pages"], alone.Order());
        Assert.Same(build, TaskDependencies.Unhooked(build, alone));
        Assert.Equal(["Brief", "Build"], TaskDependencies.Unhooked(launch, alone).BlockedBy);
    }
}
