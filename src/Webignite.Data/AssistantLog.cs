namespace Webignite.Data;

/// <summary>
/// Everything Claude and ChatGPT changed, newest first: who, when, what. When they replaced a piece of writing (a
/// description, a note, notes on a client or a person) or a client's email or name, the old value is kept here so
/// Settings, Claude and ChatGPT can put it back. Kept to the last few hundred.
/// </summary>
public static class AssistantLog
{
    private const string Name = "assistant-log";
    public const int Keep = 300;

    /// <summary>
    /// One change. <see cref="Field"/> says what <see cref="Before"/> (and <see cref="BeforeHtml"/> for formatted
    /// writing) was, when there is something to put back.
    /// </summary>
    public sealed record Entry(string Id, DateTimeOffset At, string Who, string Area, string RecordId, string What,
        string? Field = null, string? Before = null, string? BeforeHtml = null, bool PutBack = false)
    {
        public bool CanPutBack => Field is not null && !PutBack;
    }

    public static IReadOnlyList<Entry> List(LocalStore store) => store.GetSetting<List<Entry>>(Name) ?? [];

    public static Entry Add(LocalStore store, DateTimeOffset at, string who, string area, string recordId, string what,
        string? field = null, string? before = null, string? beforeHtml = null)
    {
        var entry = new Entry(Guid.CreateVersion7().ToString("N"), at, who, area, recordId, what, field, before, beforeHtml);
        store.SetSetting(Name, Kept([entry, .. List(store)], at));
        return entry;
    }

    /// <summary>How long writing that can still be put back is kept, however many changes come after it.</summary>
    public static readonly TimeSpan KeepPutBackFor = TimeSpan.FromDays(90);

    /// <summary>The most kept at all, so a runaway can't grow the setting without end.</summary>
    public const int KeepAtMost = 2000;

    /// <summary>
    /// The newest <see cref="Keep"/>, plus anything older that can still be put back and is under 90 days old. So a
    /// flood of small changes can't push out the old text of something replaced, which would amount to deleting it.
    /// </summary>
    public static List<Entry> Kept(IReadOnlyList<Entry> newestFirst, DateTimeOffset now) =>
        newestFirst.Where((e, i) => i < Keep || (e.CanPutBack && now - e.At < KeepPutBackFor)).Take(KeepAtMost).ToList();

    /// <summary>Marks a change as put back, so it isn't offered again.</summary>
    public static void MarkPutBack(LocalStore store, string id) =>
        store.SetSetting(Name, List(store).Select(e => e.Id == id ? e with { PutBack = true } : e).ToList());
}
