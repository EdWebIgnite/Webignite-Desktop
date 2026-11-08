namespace Webignite.Core.Projects;

/// <summary>
/// A message on a project's Messages tab, as Asana's: a subject and words to everyone in the project, or a reply
/// to one (<see cref="ReplyTo"/>). @mentions and likes work as on comments. Synced in shared projects.
/// </summary>
public sealed record ProjectMessage : ITracked
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }

    /// <summary>The message this answers, or null for a new conversation.</summary>
    public string? ReplyTo { get; init; }

    /// <summary>What a new conversation is about; empty on replies.</summary>
    public string Subject { get; init; } = "";
    public string Text { get; init; } = "";
    public IReadOnlyList<string> Mentions { get; init; } = [];
    public IReadOnlyList<string> LikedBy { get; init; } = [];

    public Stamp Created { get; init; }
    public Stamp Changed { get; init; }
    public Stamp? Deleted { get; init; }
    public IReadOnlyDictionary<string, Stamp> Fields { get; init; } = new Dictionary<string, Stamp>();
}

/// <summary>A conversation on the Messages tab: the message that started it and its replies, oldest first.</summary>
public sealed record MessageThread(ProjectMessage Start, IReadOnlyList<ProjectMessage> Replies)
{
    /// <summary>When anything was last said in it, for putting the liveliest first.</summary>
    public DateTimeOffset Latest => Replies.Count > 0 ? Replies[^1].Created.At : Start.Created.At;
}

public static class MessageRules
{
    public const int LongestSubject = 200;

    /// <summary>What's wrong with a message as typed; empty when it can be posted. A reply needs no subject.</summary>
    public static string Check(string subject, string text, bool reply) =>
        text.Trim().Length == 0 ? "Write something first."
        : !reply && subject.Trim().Length == 0 ? "Give it a subject, so it can be found later."
        : subject.Trim().Length > LongestSubject ? $"Keep the subject under {LongestSubject} letters."
        : "";

    /// <summary>
    /// The conversations, the one with the latest word first, each with its replies oldest first. Deleted messages
    /// are left out; replies to a deleted message go with it.
    /// </summary>
    public static IReadOnlyList<MessageThread> Threads(IEnumerable<ProjectMessage> messages)
    {
        var live = messages.Where(m => m.Deleted is null).ToList();
        var replies = live.Where(m => m.ReplyTo is not null).ToLookup(m => m.ReplyTo!, StringComparer.Ordinal);
        return [.. live.Where(m => m.ReplyTo is null)
            .Select(m => new MessageThread(m, [.. replies[m.Id].OrderBy(r => r.Created.At)]))
            .OrderByDescending(t => t.Latest)];
    }
}
