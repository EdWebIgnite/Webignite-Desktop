namespace Webignite.Data;

/// <summary>
/// The note Claude and ChatGPT leave when they change something, so the app (another process, reading the same
/// database) can show it: when, and which parts. The app checks it every few seconds and refreshes those pages.
/// </summary>
public static class AssistantSignal
{
    private const string Name = "assistant-changed";

    public sealed record Mark(DateTimeOffset At, IReadOnlyList<string> Areas);

    /// <summary>Says an area changed (projects, todos, clients, contacts, notes, calendar, time, renewals).</summary>
    public static void Touch(LocalStore store, string area, TimeProvider? clock = null)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var last = store.GetSetting<Mark>(Name);
        // Areas named since the app last looked add up for a little while, so a burst of changes refreshes them all.
        var areas = last is not null && now - last.At < TimeSpan.FromSeconds(30) ? last.Areas.Append(area).Distinct().ToList() : [area];
        store.SetSetting(Name, new Mark(now, areas));
    }

    public static Mark? Read(LocalStore store) => store.GetSetting<Mark>(Name);
}
