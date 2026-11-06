using System.Globalization;
using Webignite.Core;
using Webignite.Core.Projects;

namespace Webignite.Tests;

/// <summary>Comments and the activity feed on a task, as Asana's.</summary>
public sealed class TaskTalkTests
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-ZA");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 15, 0, TimeSpan.FromHours(2));
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");

    private static ProjectTask T(string title = "Colours") => new() { Id = "t", ProjectId = "p", Title = title };

    private static string Name(string? id) => id switch { "me" => "Eddie", "jaco" => "Jaco", _ => "someone" };

    [Fact]
    public void A_change_reads_as_what_happened_and_dull_ones_are_left_out()
    {
        var before = T() with { HeadingId = "h1", Order = 1 };
        var after = before with
        {
            Done = true, AssignedTo = "jaco", Due = new DateOnly(2026, 11, 14), HeadingId = "h2", Order = 5,
            Attachments = [new TaskAttachment("a", "Shopfront.png", 10, Now, "me")],
        };
        var changes = TaskTalk.Changes(before, after, h => h == "h2" ? "Build" : null, _ => null);
        Assert.Equal([ActivityKind.Completed, ActivityKind.Assigned, ActivityKind.DueSet, ActivityKind.Moved, ActivityKind.Attached], changes.Select(c => c.Kind));
        Assert.Empty(TaskTalk.Changes(before, before with { Order = 9 }, _ => null, _ => null));
        Assert.Equal(ActivityKind.Created, Assert.Single(TaskTalk.Changes(null, T(), _ => null, _ => null)).Kind);
        Assert.Equal(ActivityKind.SubtaskAdded, Assert.Single(TaskTalk.Changes(null, T() with { ParentId = "x" }, _ => null, _ => null)).Kind);

        string Read(ActivityKind kind, string value = "") => TaskTalk.Describe(new TaskActivity("a", "t", "p", new Stamp("me", Now), kind, value), Name, Culture);
        Assert.Equal("assigned this task to Jaco", Read(ActivityKind.Assigned, "jaco"));
        Assert.Equal("changed the due date to 14 Nov", Read(ActivityKind.DueSet, "2026-11-14"));
        Assert.Equal("moved this task to Build", Read(ActivityKind.Moved, "Build"));
        Assert.Equal("marked this task complete", Read(ActivityKind.Completed));
    }

    [Fact]
    public void Times_read_as_today_yesterday_or_the_date()
    {
        Assert.Equal("Today at 09:05", TaskTalk.When(Now.AddHours(-1).AddMinutes(-10), Now, Zone, Culture));
        Assert.Equal("Yesterday at 16:00", TaskTalk.When(new DateTimeOffset(2026, 11, 5, 16, 0, 0, TimeSpan.FromHours(2)), Now, Zone, Culture));
        Assert.Equal("1 Nov", TaskTalk.When(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(2)), Now, Zone, Culture));
        Assert.Equal("1 Nov 2025", TaskTalk.When(new DateTimeOffset(2025, 11, 1, 9, 0, 0, TimeSpan.FromHours(2)), Now, Zone, Culture));
    }

    [Fact]
    public void The_feed_shows_comments_or_everything_oldest_first()
    {
        var comment = new TaskComment { Id = "c", TaskId = "t", ProjectId = "p", Text = "Photos are in", Created = new Stamp("jaco", Now) };
        var gone = comment with { Id = "d", Text = "Oops", Deleted = new Stamp("jaco", Now) };
        var made = new TaskActivity("a", "t", "p", new Stamp("me", Now.AddHours(-2)), ActivityKind.Created, "Colours");
        var comments = TaskTalk.Feed([comment, gone], [made], all: false, Name, Culture);
        Assert.Equal(["Photos are in"], comments.Select(l => l.Text));
        var all = TaskTalk.Feed([comment, gone], [made], all: true, Name, Culture);
        Assert.Equal(["created this task", "Photos are in"], all.Select(l => l.Text));
        Assert.Equal(["Eddie", "Jaco"], all.Select(l => l.Who));
        Assert.NotEmpty(TaskTalk.Check("  "));
        Assert.Empty(TaskTalk.Check("Done"));
    }
}
