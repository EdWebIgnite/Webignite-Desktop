using System.Globalization;

namespace Webignite.Core.Projects;

/// <summary>A comment on a task, as Asana's. Kept on this PC for now; stamped so it can sync later.</summary>
public sealed record TaskComment : ITracked
{
    public required string Id { get; init; }
    public required string TaskId { get; init; }
    public required string ProjectId { get; init; }
    public string Text { get; init; } = "";

    public Stamp Created { get; init; }
    public Stamp Changed { get; init; }
    public Stamp? Deleted { get; init; }
    public IReadOnlyDictionary<string, Stamp> Fields { get; init; } = new Dictionary<string, Stamp>();
}

/// <summary>What happened to a task, for its All activity feed.</summary>
public enum ActivityKind
{
    Created, Completed, Reopened, Assigned, Unassigned, DueSet, DueCleared, Moved, Renamed, DescriptionChanged,
    Attached, Detached, SubtaskAdded, ProjectChanged, PriorityChanged,
}

/// <summary>
/// One thing that happened to a task: who, when, what kind, and the value it needs to read well (a person's id,
/// a date, a heading or file name). Written once, never changed.
/// </summary>
public sealed record TaskActivity(string Id, string TaskId, string ProjectId, Stamp At, ActivityKind Kind, string Value = "");

/// <summary>A line in a task's feed: a comment, or something that happened.</summary>
public sealed record FeedLine(DateTimeOffset At, string Who, string Text, bool IsComment, string? CommentId = null);

/// <summary>Comments and the activity feed, as Asana's.</summary>
public static class TaskTalk
{
    public static string Check(string text) => text.Trim().Length == 0 ? "Write something first." : "";

    /// <summary>
    /// What changed between a task as it was and as it is, as feed entries. Heading and project names are looked up
    /// by id, so they read as words. Nothing comes back for a change nobody would want to see (its order, say).
    /// </summary>
    public static IReadOnlyList<(ActivityKind Kind, string Value)> Changes(ProjectTask? before, ProjectTask after,
        Func<string?, string?> headingName, Func<string, string?> projectName)
    {
        if (before is null)
            return [(after.ParentId is null ? ActivityKind.Created : ActivityKind.SubtaskAdded, after.Title)];
        var changes = new List<(ActivityKind, string)>();
        if (!before.Done && after.Done)
            changes.Add((ActivityKind.Completed, ""));
        else if (before.Done && !after.Done)
            changes.Add((ActivityKind.Reopened, ""));
        if (before.AssignedTo != after.AssignedTo)
            changes.Add(after.AssignedTo is { } who ? (ActivityKind.Assigned, who) : (ActivityKind.Unassigned, ""));
        if (before.Due != after.Due)
            changes.Add(after.Due is { } due ? (ActivityKind.DueSet, due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : (ActivityKind.DueCleared, ""));
        if (before.ProjectId != after.ProjectId)
            changes.Add((ActivityKind.ProjectChanged, projectName(after.ProjectId) ?? ""));
        else if (before.HeadingId != after.HeadingId && after.ParentId is null)
            changes.Add((ActivityKind.Moved, headingName(after.HeadingId) ?? "No heading"));
        if (before.Title != after.Title && before.Title.Length > 0)
            changes.Add((ActivityKind.Renamed, after.Title));
        if (before.Notes != after.Notes || before.NotesHtml != after.NotesHtml)
            changes.Add((ActivityKind.DescriptionChanged, ""));
        if (before.Priority != after.Priority)
            changes.Add((ActivityKind.PriorityChanged, after.Priority.ToString()));
        foreach (var added in after.Attachments.Where(a => before.Attachments.All(b => b.Id != a.Id)))
            changes.Add((ActivityKind.Attached, added.FileName));
        foreach (var gone in before.Attachments.Where(b => after.Attachments.All(a => a.Id != b.Id)))
            changes.Add((ActivityKind.Detached, gone.FileName));
        return changes;
    }

    /// <summary>How a feed entry reads, after the person's name: "marked this task complete", "changed the due date to 14 Nov".</summary>
    public static string Describe(TaskActivity activity, Func<string?, string> nameOf, IFormatProvider culture) => activity.Kind switch
    {
        ActivityKind.Created => "created this task",
        ActivityKind.SubtaskAdded => $"added the subtask {activity.Value}",
        ActivityKind.Completed => "marked this task complete",
        ActivityKind.Reopened => "marked this task not done",
        ActivityKind.Assigned => $"assigned this task to {nameOf(activity.Value)}",
        ActivityKind.Unassigned => "took this task off everyone",
        ActivityKind.DueSet => DateOnly.TryParseExact(activity.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due)
            ? $"changed the due date to {due.ToString("d MMM", culture)}" : "changed the due date",
        ActivityKind.DueCleared => "took the due date off",
        ActivityKind.Moved => $"moved this task to {activity.Value}",
        ActivityKind.ProjectChanged => $"moved this task to the project {activity.Value}",
        ActivityKind.Renamed => $"renamed this task to {activity.Value}",
        ActivityKind.DescriptionChanged => "changed the description",
        ActivityKind.PriorityChanged => $"set the priority to {activity.Value}",
        ActivityKind.Attached => $"attached {activity.Value}",
        ActivityKind.Detached => $"removed {activity.Value}",
        _ => "changed this task",
    };

    /// <summary>When something happened, as Asana's feed says it: "Today at 10:15", "Yesterday at 16:02", "4 Nov", "4 Nov 2025".</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone, IFormatProvider culture)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone).DateTime;
        var day = DateOnly.FromDateTime(local);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var time = local.ToString("HH:mm", culture);
        if (day == today)
            return $"Today at {time}";
        if (day == today.AddDays(-1))
            return $"Yesterday at {time}";
        return day.Year == today.Year ? day.ToString("d MMM", culture) : day.ToString("d MMM yyyy", culture);
    }

    /// <summary>
    /// The task's feed, oldest first as Asana's: its comments, and with <paramref name="all"/> everything that
    /// happened too. Deleted comments are left out.
    /// </summary>
    public static IReadOnlyList<FeedLine> Feed(IEnumerable<TaskComment> comments, IEnumerable<TaskActivity> activity, bool all,
        Func<string?, string> nameOf, IFormatProvider culture)
    {
        var lines = comments.Where(c => c.Deleted is null)
            .Select(c => new FeedLine(c.Created.At, nameOf(c.Created.By), c.Text, true, c.Id)).ToList();
        if (all)
            lines.AddRange(activity.Select(a => new FeedLine(a.At.At, nameOf(a.At.By), Describe(a, nameOf, culture), false)));
        return [.. lines.OrderBy(l => l.At)];
    }
}
