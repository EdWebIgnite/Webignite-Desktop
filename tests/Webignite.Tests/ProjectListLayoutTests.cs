using Webignite.Core;
using Webignite.Core.Projects;

namespace Webignite.Tests;

/// <summary>A project's list as Asana lays it out: sorted, grouped and folded.</summary>
public sealed class ProjectListLayoutTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static readonly Heading Design = new() { Id = "h1", ProjectId = "p", Name = "Design", Order = 1 };
    private static readonly Heading Build = new() { Id = "h2", ProjectId = "p", Name = "Build", Order = 2 };

    private static ProjectTask T(string title, double order, string? heading = null, DateOnly? due = null, string? who = null,
        TaskPriority priority = TaskPriority.Normal, string? parent = null, int made = 0) => new()
    {
        Id = title, ProjectId = "p", Title = title, Order = order, HeadingId = heading, Due = due, AssignedTo = who, Priority = priority,
        ParentId = parent, Created = new Stamp("me", DateTimeOffset.UnixEpoch.AddDays(made)),
    };

    private static string? Name(string? id) => id switch { "me" => "Eddie", "jaco" => "Jaco", _ => null };

    private static readonly ProjectTask[] Tasks =
    [
        T("Wireframes", 1, "h1", Today.AddDays(1), "me", made: 3),
        T("Colours", 2, "h1", Today.AddDays(-1), "jaco", TaskPriority.High, made: 1),
        T("Build pages", 1, "h2", null, null, TaskPriority.Low, made: 2),
        T("Logo", 1, null, Today, "me", made: 4),
        T("Mobile layout", 1, "h1", parent: "Wireframes"),
    ];

    [Fact]
    public void By_sections_the_list_keeps_the_dragged_order_and_leaves_subtasks_out()
    {
        var groups = ProjectList.Arrange([Design, Build], Tasks, new ProjectLayout(), Name, Today);
        Assert.Equal(["No heading", "Design", "Build"], groups.Select(g => g.Title));
        Assert.Equal(["Wireframes", "Colours"], groups[1].Tasks.Select(t => t.Title));
        Assert.All(groups, g => Assert.True(g.IsSection));
        Assert.True(ProjectList.CanReorder(new ProjectLayout()));
    }

    [Fact]
    public void A_sort_orders_each_section_and_stops_dragging_into_order()
    {
        var layout = new ProjectLayout { Sort = TaskSort.DueDate };
        var groups = ProjectList.Arrange([Design, Build], Tasks, layout, Name, Today);
        Assert.Equal(["Colours", "Wireframes"], groups[1].Tasks.Select(t => t.Title));
        Assert.False(ProjectList.CanReorder(layout));
        Assert.Equal(["Colours", "Build pages", "Wireframes", "Logo"],
            ProjectList.Sort(Tasks.Where(t => t.ParentId is null), TaskSort.Created, Name).Select(t => t.Title));
        Assert.Equal(["Build pages", "Colours", "Logo", "Wireframes"],
            ProjectList.Sort(Tasks.Where(t => t.ParentId is null), TaskSort.Alphabetical, Name).Select(t => t.Title));
    }

    [Fact]
    public void Grouping_by_when_who_or_priority_makes_its_own_groups()
    {
        var byDue = ProjectList.Arrange([Design, Build], Tasks, new ProjectLayout { Group = TaskGroup.DueDate }, Name, Today);
        Assert.Equal(["Overdue", "Today", "This week", "No due date"], byDue.Select(g => g.Title));
        Assert.All(byDue, g => Assert.False(g.IsSection));
        var byWho = ProjectList.Arrange([Design, Build], Tasks, new ProjectLayout { Group = TaskGroup.Assignee }, Name, Today);
        Assert.Equal(["Eddie", "Jaco", "No one"], byWho.Select(g => g.Title));
        var byPriority = ProjectList.Arrange([Design, Build], Tasks, new ProjectLayout { Group = TaskGroup.Priority }, Name, Today);
        Assert.Equal(["High", "Normal", "Low"], byPriority.Select(g => g.Title));
        Assert.False(ProjectList.CanReorder(new ProjectLayout { Group = TaskGroup.Priority }));
    }

    [Fact]
    public void A_heading_folds_up_and_opens_out_once_each()
    {
        var folded = ProjectList.Fold(new ProjectLayout(), "h1", true);
        Assert.Equal(["h1"], folded.Folded);
        Assert.Equal(["h1"], ProjectList.Fold(folded, "h1", true).Folded);
        Assert.Empty(ProjectList.Fold(folded, "h1", false).Folded);
        Assert.Equal("Due date", ProjectList.Label(TaskSort.DueDate));
        Assert.Equal("Sections", ProjectList.Label(TaskGroup.Sections));
    }
}
