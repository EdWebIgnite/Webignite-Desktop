namespace Webignite.Core.Layout;

/// <summary>One keyboard shortcut as it is listed: the keys, what they do, and the part of the app they belong to.</summary>
public sealed record Shortcut(string Keys, string What, string Group);

/// <summary>A heading in the shortcut list with the shortcuts under it.</summary>
public sealed record ShortcutGroup(string Name, IReadOnlyList<Shortcut> Items);

/// <summary>
/// How the list of keyboard shortcuts is shown (F1, and Settings): under headings, the page you are on first, and
/// narrowed by what is typed in its search box.
/// </summary>
public static class ShortcutList
{
    /// <summary>The headings, in the order they are listed when no page puts its own first.</summary>
    public const string Everywhere = "Everywhere in the app";
    public const string AnyProgram = "From any program";
    public const string Tabs = "Tabs";
    public const string Lists = "Clients, Quotes and Invoices";
    public const string Mail = "Mail";
    public const string Writing = "Writing";
    public const string Notes = "Notes";
    public const string Files = "Files";
    public const string Tools = "Tools";

    /// <summary>
    /// The shortcuts under their headings, in the order the headings first appear, with <paramref name="first"/>
    /// (the heading of the page that is open) moved to the top. Typing narrows it: every word typed must be in
    /// the keys, in what they do, or in the heading. Headings left with nothing are dropped.
    /// </summary>
    public static IReadOnlyList<ShortcutGroup> Show(IEnumerable<Shortcut> all, string? typed = null, string? first = null)
    {
        var words = (typed ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var groups = new List<(string Name, List<Shortcut> Items)>();
        foreach (var shortcut in all)
        {
            if (!words.All(w => Has(shortcut, w)))
                continue;
            var at = groups.FindIndex(g => g.Name == shortcut.Group);
            if (at < 0)
            {
                groups.Add((shortcut.Group, []));
                at = groups.Count - 1;
            }
            groups[at].Items.Add(shortcut);
        }
        var mine = groups.FindIndex(g => g.Name == first);
        if (mine > 0)
        {
            var moved = groups[mine];
            groups.RemoveAt(mine);
            groups.Insert(0, moved);
        }
        return [.. groups.Select(g => new ShortcutGroup(g.Name, g.Items))];
    }

    /// <summary>How many shortcuts are showing, for "No shortcut found" and the count beside the search box.</summary>
    public static int Count(IEnumerable<ShortcutGroup> groups) => groups.Sum(g => g.Items.Count);

    // "ctrl k" and "ctrl+k" both find Ctrl+K: a word is looked for in the keys with and without the plus signs.
    private static bool Has(Shortcut shortcut, string word) =>
        shortcut.Keys.Contains(word, StringComparison.OrdinalIgnoreCase)
        || shortcut.Keys.Replace("+", " ", StringComparison.Ordinal).Contains(word, StringComparison.OrdinalIgnoreCase)
        || shortcut.What.Contains(word, StringComparison.OrdinalIgnoreCase)
        || shortcut.Group.Contains(word, StringComparison.OrdinalIgnoreCase);
}
