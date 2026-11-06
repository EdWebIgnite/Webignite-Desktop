using Webignite.Core.Projects;

namespace Webignite.Tests;

/// <summary>Planner's Home: the greeting, my tasks in tabs, and recent projects.</summary>
public sealed class PlannerHomeTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);
    private static readonly Stamp Then = new("me", new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(2)));

    private static ProjectTask Task(string title, DateOnly? due = null, bool done = false, string? to = "me", DateTimeOffset? doneAt = null) => new()
    {
        Id = title,
        ProjectId = "p",
        Title = title,
        Due = due,
        Done = done,
        AssignedTo = to,
        Created = Then,
        Changed = Then,
        Fields = doneAt is { } at ? new Dictionary<string, Stamp> { ["done"] = new("me", at) } : new Dictionary<string, Stamp>(),
    };

    [Theory]
    [InlineData(8, "Good morning")]
    [InlineData(13, "Good afternoon")]
    [InlineData(19, "Good evening")]
    public void The_greeting_follows_the_hour(int hour, string greeting) => Assert.Equal(greeting, PlannerHome.Greeting(hour));

    [Fact]
    public void Upcoming_is_soonest_first_with_undated_last_and_overdue_is_apart()
    {
        var mine = new[] { Task("No date"), Task("Late", Today.AddDays(-2)), Task("Friday", Today.AddDays(3)), Task("Today", Today), Task("Done", Today, done: true) };
        Assert.Equal(["Today", "Friday", "No date"], PlannerHome.Upcoming(mine, Today).Select(t => t.Title));
        Assert.Equal(["Late"], PlannerHome.Overdue(mine, Today).Select(t => t.Title));
    }

    [Fact]
    public void Completed_is_mine_ticked_off_since_a_moment_newest_first()
    {
        var monday = PlannerHome.WeekStart(new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.FromHours(2)));
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(2)), monday);
        var tasks = new[]
        {
            Task("Tuesday", done: true, doneAt: monday.AddDays(1)),
            Task("Thursday", done: true, doneAt: monday.AddDays(3)),
            Task("Last week", done: true, doneAt: monday.AddDays(-2)),
            Task("Someone else's", done: true, to: "partner", doneAt: monday.AddDays(2)),
            Task("Still open"),
        };
        Assert.Equal(["Thursday", "Tuesday"], PlannerHome.Completed(tasks, "me", monday).Select(t => t.Title));
    }

    [Fact]
    public void Recent_projects_are_the_live_ones_changed_lately()
    {
        Project P(string name, int day, ProjectStatus status = ProjectStatus.Active) => new()
        {
            Id = name, Name = name, Status = status, Created = Then, Changed = Then with { At = Then.At.AddDays(day) },
        };
        var recent = PlannerHome.Recent([P("Old", 1), P("New", 4), P("Finished", 5, ProjectStatus.Done), P("Middle", 2)], 2);
        Assert.Equal(["New", "Middle"], recent.Select(p => p.Name));
    }
}
