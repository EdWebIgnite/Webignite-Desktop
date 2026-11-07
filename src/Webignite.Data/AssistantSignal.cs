namespace Webignite.Data;

/// <summary>
/// The note Claude and ChatGPT leave when they change something, so the app (another process, reading the same
/// database) can show it: when each part last changed. The app checks it every few seconds and refreshes only the
/// parts changed since it last looked.
/// </summary>
public static class AssistantSignal
{
    private const string Name = "assistant-changes";

    /// <summary>When anything last changed, and when each area did (projects, todos, clients, contacts, notes, calendar, time, renewals).</summary>
    public sealed record Mark(DateTimeOffset At, IReadOnlyDictionary<string, DateTimeOffset> Areas)
    {
        /// <summary>The areas changed after <paramref name="seen"/>.</summary>
        public IReadOnlyList<string> Since(DateTimeOffset seen) => [.. Areas.Where(a => a.Value > seen).Select(a => a.Key).Order(StringComparer.Ordinal)];
    }

    /// <summary>Says an area changed now.</summary>
    public static void Touch(LocalStore store, string area, TimeProvider? clock = null)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var areas = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        if (store.GetSetting<Mark>(Name) is { } last)
            foreach (var (name, at) in last.Areas)
                areas[name] = at;
        areas[area] = now;
        store.SetSetting(Name, new Mark(now, areas));
    }

    public static Mark? Read(LocalStore store) => store.GetSetting<Mark>(Name);
}
