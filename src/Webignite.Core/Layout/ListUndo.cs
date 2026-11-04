namespace Webignite.Core.Layout;

/// <summary>Undo for a removed item in a list kept as a whole, such as mail rules or saved replies.</summary>
public static class ListUndo
{
    /// <summary>
    /// Puts <paramref name="removed"/> back in the list as it is now, at the place it had, so anything added,
    /// switched or removed since stays as it is. An item already there (Undo clicked twice) is left alone.
    /// </summary>
    public static IReadOnlyList<T> PutBack<T>(IReadOnlyList<T> now, T removed, int at, Func<T, string> id)
    {
        var key = id(removed);
        if (now.Any(item => id(item) == key))
            return now;
        var list = now.ToList();
        list.Insert(Math.Clamp(at, 0, list.Count), removed);
        return list;
    }
}
