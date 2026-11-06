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

/// <summary>Home's Projects card: recent, starred, or all in my order.</summary>
public sealed class HomeProjectsTests
{
    private static Project P(string name, double order, bool star = false, int changed = 0, ProjectStatus status = ProjectStatus.Active) => new()
    {
        Id = name, Name = name, Order = order, Favourite = star, Status = status,
        Changed = new Stamp("me", DateTimeOffset.UnixEpoch.AddDays(changed)),
    };

    [Fact]
    public void The_card_shows_recent_starred_or_all_in_order_and_never_done_ones()
    {
        Project[] all = [P("A", 1, changed: 5), P("B", 2, star: true, changed: 1), P("C", 3, changed: 9), P("D", 4, star: true), P("Done", 0, changed: 20, status: ProjectStatus.Done)];
        Assert.Equal(["C", "A", "B", "D"], PlannerHome.Shown(all, HomeProjects.Recent, 8).Select(p => p.Name));
        Assert.Equal(["B", "D"], PlannerHome.Shown(all, HomeProjects.Starred, 8).Select(p => p.Name));
        Assert.Equal(["B", "D", "A", "C"], PlannerHome.Shown(all, HomeProjects.Ordered, 8).Select(p => p.Name));
        Assert.Equal(["B", "D"], PlannerHome.Shown(all, HomeProjects.Ordered, 2).Select(p => p.Name));
        Assert.Equal("In my order", PlannerHome.Label(HomeProjects.Ordered));
    }
}

/// <summary>A project's look: its icon, its colour and its status, as Asana shows them.</summary>
public sealed class ProjectLooksTests
{
    private static readonly string[] Names = ["AMOS", "Copywriting", "Forus Training", "POOT!", "Vida Pets", "Webignite"];

    private static Project P(string name, string icon = "", string colour = "", bool favourite = false, double order = 0) => new()
    {
        Id = name, Name = name, Icon = icon, Colour = colour, Favourite = favourite, Order = order,
    };

    [Fact]
    public void A_picked_icon_and_colour_are_kept_and_unknown_ones_fall_back()
    {
        Assert.Equal("Star", ProjectLooks.IconOf(P("A", icon: "Star")));
        Assert.Equal("List", ProjectLooks.IconOf(P("A", icon: "Nonsense")));
        Assert.Equal("Red", ProjectLooks.ColourOf(P("A", colour: "Red")));
        Assert.Equal(ProjectLooks.ColourFor("Amos Website"), ProjectLooks.ColourOf(P("Amos Website", colour: "Mauve")));
        Assert.Equal(32, ProjectLooks.Icons.Count);
        Assert.Equal(16, ProjectLooks.Colours.Count);
    }

    [Fact]
    public void Looks_picked_before_Asanas_set_came_in_are_kept_close()
    {
        Assert.Equal("BlueGreen", ProjectLooks.ColourOf(P("A", colour: "Teal")));
        Assert.Equal("CoolGrey", ProjectLooks.ColourOf(P("A", colour: "Grey")));
        Assert.Equal("Board", ProjectLooks.IconOf(P("A", icon: "Tiles")));
        Assert.Equal("Coins", ProjectLooks.IconOf(P("A", icon: "Money")));
        Assert.Equal("Cool grey", ProjectLooks.ColourLabel("CoolGrey"));
        Assert.Equal("Light bulb", ProjectLooks.IconLabel("LightBulb"));
    }

    [Fact]
    public void A_name_always_gets_the_same_colour_and_never_grey_by_chance()
    {
        Assert.Equal(ProjectLooks.ColourFor("Vida Pets"), ProjectLooks.ColourFor(" vida pets "));
        var picked = Names.Select(ProjectLooks.ColourFor).ToList();
        Assert.DoesNotContain("CoolGrey", picked);
        Assert.DoesNotContain("None", picked);
        Assert.True(picked.Distinct().Count() > 2);
    }

    [Fact]
    public void Status_reads_as_Asana_and_starred_projects_come_first()
    {
        Assert.Equal("At risk", ProjectLooks.Label(ProjectHealth.AtRisk));
        Assert.Equal("Set status", ProjectLooks.Label(ProjectHealth.None));
        var order = ProjectLooks.SidebarOrder([P("One", order: 1), P("Two", order: 2, favourite: true), P("Three", order: 3)]);
        Assert.Equal(["Two", "One", "Three"], order.Select(p => p.Name));
    }

    [Fact]
    public void Changing_the_look_is_stamped_for_sync()
    {
        var now = new Stamp("me", DateTimeOffset.UnixEpoch);
        var before = ProjectRules.Stamped(null, P("A"), now);
        var after = ProjectRules.Stamped(before, before with { Colour = "Blue", Favourite = true }, now with { At = now.At.AddMinutes(1) });
        Assert.True(after.Fields.ContainsKey("colour") && after.Fields.ContainsKey("favourite"));
    }
}
