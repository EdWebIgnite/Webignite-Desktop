namespace Webignite.Core.Layout;

/// <summary>
/// A page's folding rows (the client page's To-do, Quotes and invoices, Mail and so on): their order top to
/// bottom, and which are open. Saved per page, so it opens the way it was left.
/// </summary>
public sealed record SectionLayout
{
    public IReadOnlyList<string> Order { get; init; } = [];
    public IReadOnlyList<string> Open { get; init; } = [];

    public bool IsOpen(string key) => Open.Contains(key, StringComparer.Ordinal);
}

public static class Sections
{
    /// <summary>
    /// A saved layout against the rows the page has now: rows added since go at the bottom, closed; rows that
    /// no longer exist are dropped. With nothing saved, the rows in their usual order with the usual ones open.
    /// </summary>
    public static SectionLayout Reconcile(SectionLayout? saved, IReadOnlyList<string> keys, IReadOnlyList<string> openAtFirst)
    {
        if (saved is null)
            return new SectionLayout { Order = [.. keys], Open = [.. openAtFirst.Where(keys.Contains)] };
        var order = saved.Order.Where(keys.Contains).Distinct().ToList();
        order.AddRange(keys.Where(k => !order.Contains(k, StringComparer.Ordinal)));
        return new SectionLayout { Order = order, Open = [.. saved.Open.Where(keys.Contains).Distinct()] };
    }

    public static SectionLayout Toggle(SectionLayout layout, string key) => layout with
    {
        Open = layout.IsOpen(key) ? [.. layout.Open.Where(k => k != key)] : [.. layout.Open, key],
    };

    /// <summary>Opens every row, or closes them all.</summary>
    public static SectionLayout All(SectionLayout layout, bool open) => layout with { Open = open ? [.. layout.Order] : [] };

    /// <summary>Moves a row to a place in the list (0 is the top), as when it's dragged there.</summary>
    public static SectionLayout MoveTo(SectionLayout layout, string key, int index)
    {
        var order = layout.Order.ToList();
        var from = order.IndexOf(key);
        if (from < 0)
            return layout;
        order.RemoveAt(from);
        // Dropped below where it was: the gap it left counts.
        if (index > from)
            index--;
        order.Insert(Math.Clamp(index, 0, order.Count), key);
        return layout with { Order = order };
    }

    /// <summary>Moves a row one up (-1) or down (+1).</summary>
    public static SectionLayout MoveBy(SectionLayout layout, string key, int by)
    {
        var at = layout.Order.ToList().IndexOf(key);
        return at < 0 ? layout : MoveTo(layout, key, by < 0 ? at - 1 : at + 2);
    }
}
