namespace Webignite.Core.Overview;

/// <summary>A note the app showed in its corner, kept so it can be read later: what it said and when.</summary>
public sealed record KeptNote(string Id, string Title, string Text, DateTimeOffset At, bool Seen = false);

/// <summary>
/// The app's notifications, kept like Windows keeps its own: the newest first, each one there until it is
/// dismissed, at most <see cref="Most"/>. The notes that only offer Undo are not kept; they mean nothing later.
/// </summary>
public static class NoteHistory
{
    public const int Most = 100;
    public const string UndoText = "Click here to undo.";

    /// <summary>Whether a note is worth keeping: anything but an offer to undo.</summary>
    public static bool Keeps(string text) => text != UndoText;

    /// <summary>The history with a note added at the top. One saying the same within a minute of the last is not added again.</summary>
    public static IReadOnlyList<KeptNote> Add(IReadOnlyList<KeptNote> kept, string title, string text, DateTimeOffset now)
    {
        if (!Keeps(text) || (kept.Count > 0 && kept[0].Title == title && kept[0].Text == text && now - kept[0].At < TimeSpan.FromMinutes(1)))
            return kept;
        return [new KeptNote(Guid.CreateVersion7().ToString("N"), title, text, now), .. kept.Take(Most - 1)];
    }

    public static IReadOnlyList<KeptNote> Dismiss(IReadOnlyList<KeptNote> kept, string id) => [.. kept.Where(n => n.Id != id)];

    /// <summary>Every note marked as seen, when the list is opened.</summary>
    public static IReadOnlyList<KeptNote> Seen(IReadOnlyList<KeptNote> kept) => kept.All(n => n.Seen) ? kept : [.. kept.Select(n => n with { Seen = true })];

    public static int Unseen(IReadOnlyList<KeptNote> kept) => kept.Count(n => !n.Seen);
}
