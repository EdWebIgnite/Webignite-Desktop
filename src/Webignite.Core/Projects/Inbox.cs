namespace Webignite.Core.Projects;

public enum InboxKind { Assigned, Mentioned, Commented, Completed, Changed, Liked }

/// <summary>
/// Something your partner did that you'd want to know about, as Asana's Inbox: a task given to you, a comment
/// that mentions you or is on a task you follow, a change to a task you follow, a like on what you made.
/// </summary>
/// <param name="Key">Stays the same every time it's worked out, so read and archived can be remembered.</param>
/// <param name="Line">What happened, after the person's name: "commented", "assigned this task to you".</param>
/// <param name="Snippet">The comment's words, when it's about one.</param>
public sealed record InboxItem(string Key, InboxKind Kind, string TaskId, string ProjectId, string TaskTitle, string WhoId, string Who,
    DateTimeOffset At, string Line, string Snippet = "");

/// <summary>What's been read and archived in Inbox on this PC (setting <c>inbox</c>), and from when it starts.</summary>
public sealed record InboxState
{
    /// <summary>Nothing older shows, so the first sync doesn't bring months of old news.</summary>
    public DateTimeOffset? Since { get; init; }
    public IReadOnlyList<string> Read { get; init; } = [];
    public IReadOnlyList<string> Archived { get; init; } = [];
}

public static class InboxRules
{
    /// <summary>How far back Inbox starts the first time: two weeks.</summary>
    public static readonly TimeSpan FirstLook = TimeSpan.FromDays(14);

    /// <summary>The most read and archived marks kept; the oldest go first.</summary>
    public const int Remembered = 3000;

    private const int SnippetLength = 160;

    /// <summary>
    /// What's in my Inbox, newest first: things others did since <paramref name="since"/> on live tasks I follow or
    /// am given, comments mentioning me, and likes on my comments and tasks. Never what I did myself, nor what
    /// Claude or ChatGPT did (they work for one of us, who already knows).
    /// </summary>
    public static IReadOnlyList<InboxItem> Build(string me, IEnumerable<ProjectTask> tasks, IEnumerable<TaskComment> comments,
        IEnumerable<TaskActivity> activity, DateTimeOffset since, Func<string?, string> nameOf, IFormatProvider culture)
    {
        var live = tasks.Where(t => t.Deleted is null).ToDictionary(t => t.Id, StringComparer.Ordinal);
        bool Mine(ProjectTask t) => t.Followers.Contains(me) || t.AssignedTo == me;
        bool Others(string by) => by != me && !Actors.IsAssistant(by);
        var items = new List<InboxItem>();
        InboxItem Item(string key, InboxKind kind, ProjectTask task, string by, DateTimeOffset at, string line, string snippet = "") =>
            new(key, kind, task.Id, task.ProjectId, task.Title, by, nameOf(by), at, line, snippet);

        foreach (var a in activity)
        {
            if (a.At.At < since || !Others(a.At.By) || a.Kind == ActivityKind.Created || !live.TryGetValue(a.TaskId, out var task))
                continue;
            if (a.Kind == ActivityKind.Assigned && a.Value == me)
                items.Add(Item("activity:" + a.Id, InboxKind.Assigned, task, a.At.By, a.At.At, "assigned this task to you"));
            else if (Mine(task))
                items.Add(Item("activity:" + a.Id, a.Kind == ActivityKind.Completed ? InboxKind.Completed : InboxKind.Changed, task, a.At.By, a.At.At,
                    TaskTalk.Describe(a, nameOf, culture)));
        }
        foreach (var c in comments)
        {
            if (c.Deleted is not null || !live.TryGetValue(c.TaskId, out var task))
                continue;
            if (c.Created.At >= since && Others(c.Created.By))
            {
                if (c.Mentions.Contains(me))
                    items.Add(Item("comment:" + c.Id, InboxKind.Mentioned, task, c.Created.By, c.Created.At, "mentioned you", Snip(c.Text)));
                else if (Mine(task))
                    items.Add(Item("comment:" + c.Id, InboxKind.Commented, task, c.Created.By, c.Created.At, "commented", Snip(c.Text)));
            }
            if (c.Created.By == me)
                items.AddRange(Likes(c.Id, c.LikedBy, c.Fields, since, Others).Select(l => Item(l.Key, InboxKind.Liked, task, l.By, l.At, "liked your comment", Snip(c.Text))));
        }
        foreach (var task in live.Values.Where(t => t.Created.By == me))
            items.AddRange(Likes(task.Id, task.LikedBy, task.Fields, since, Others).Select(l => Item(l.Key, InboxKind.Liked, task, l.By, l.At, "liked your task")));
        return [.. items.OrderByDescending(i => i.At).ThenBy(i => i.Key, StringComparer.Ordinal)];
    }

    // A like counts from when it was given (its own stamp), and only while it's still there.
    private static IEnumerable<(string Key, string By, DateTimeOffset At)> Likes(string id, IReadOnlyList<string> likedBy,
        IReadOnlyDictionary<string, Stamp> fields, DateTimeOffset since, Func<string, bool> others) =>
        likedBy.Where(others)
            .Select(by => (By: by, Stamp: fields.TryGetValue(Sets.Key(Sets.Like, by), out var s) ? s : (Stamp?)null))
            .Where(l => l.Stamp is { } s && s.At >= since)
            .Select(l => ($"like:{id}:{l.By}", l.By, l.Stamp!.Value.At));

    private static string Snip(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= SnippetLength ? flat : flat[..(SnippetLength - 1)].TrimEnd() + "…";
    }

    /// <summary>The state with Inbox's starting point set the first time it's looked at.</summary>
    public static InboxState Started(InboxState state, DateTimeOffset now) => state.Since is null ? state with { Since = now - FirstLook } : state;

    public static int Unread(IEnumerable<InboxItem> items, InboxState state)
    {
        var done = state.Read.Concat(state.Archived).ToHashSet(StringComparer.Ordinal);
        return items.Count(i => !done.Contains(i.Key));
    }

    public static InboxState MarkRead(InboxState state, IEnumerable<string> keys) => state with { Read = Keep(state.Read, keys) };

    public static InboxState Archive(InboxState state, string key) => state with { Archived = Keep(state.Archived, [key]), Read = Keep(state.Read, [key]) };

    public static InboxState Unarchive(InboxState state, string key) => state with { Archived = [.. state.Archived.Where(k => k != key)] };

    private static List<string> Keep(IReadOnlyList<string> kept, IEnumerable<string> more)
    {
        var all = kept.Concat(more.Where(k => !kept.Contains(k))).Distinct(StringComparer.Ordinal).ToList();
        return all.Count <= Remembered ? all : all[^Remembered..];
    }

    /// <summary>Which group an item sits in, as Asana's Inbox: Today, Yesterday, This week, Earlier.</summary>
    public static string Group(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return (today.DayNumber - day.DayNumber) switch
        {
            <= 0 => "Today",
            1 => "Yesterday",
            < 7 => "This week",
            _ => "Earlier",
        };
    }

    /// <summary>A note's heading for something new: "Lize commented on Write the copy".</summary>
    public static string Headline(InboxItem item) => item.Kind switch
    {
        InboxKind.Assigned => $"{item.Who} gave you {item.TaskTitle}",
        InboxKind.Mentioned => $"{item.Who} mentioned you on {item.TaskTitle}",
        InboxKind.Commented => $"{item.Who} commented on {item.TaskTitle}",
        InboxKind.Completed => $"{item.Who} completed {item.TaskTitle}",
        InboxKind.Liked => $"{item.Who} {item.Line}",
        _ => $"{item.Who} changed {item.TaskTitle}",
    };

}
