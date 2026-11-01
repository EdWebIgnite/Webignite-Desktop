namespace Webignite.Core.Documents;

/// <summary>A standard package or item, e.g. "Website, 6 pages" with its usual lines and prices.</summary>
public sealed record PriceEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<Line> Lines { get; init; } = [];

    public decimal Total => Lines.Sum(l => l.Amount());

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

public static class PriceList
{
    /// <summary>The lines to put on a document: the package's own, ready to change for this client.</summary>
    public static IReadOnlyList<Line> LinesFor(PriceEntry entry) =>
        entry.Lines.Select(l => l with { SourceLineTotal = null }).ToList();

    /// <summary>
    /// Saves a package made from a document's lines. A package with the same name (ignoring case) is replaced,
    /// so saving again updates it rather than making a second one. Sorted by name.
    /// </summary>
    public static IReadOnlyList<PriceEntry> Save(IReadOnlyList<PriceEntry> list, string name, IEnumerable<Line> lines)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("Give the package a name.", nameof(name));
        var kept = lines.Where(l => !string.IsNullOrWhiteSpace(l.Description)).Select(l => l with { SourceLineTotal = null }).ToList();
        if (kept.Count == 0)
            throw new ArgumentException("Add at least one line first.", nameof(lines));
        var existing = list.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        var entry = new PriceEntry { Id = existing?.Id ?? PriceEntry.NewId(), Name = name, Lines = kept };
        return list.Where(e => e.Id != entry.Id).Append(entry).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<PriceEntry> Remove(IReadOnlyList<PriceEntry> list, string id) => list.Where(e => e.Id != id).ToList();

    /// <summary>
    /// Adds a package's lines to the lines already on a document. Blank lines are dropped first, so picking a
    /// package on a new document doesn't leave an empty line at the top.
    /// </summary>
    public static IReadOnlyList<Line> AddTo(IEnumerable<Line?> current, PriceEntry entry) =>
        current.OfType<Line>().Where(l => !string.IsNullOrWhiteSpace(l.Description) || l.UnitPrice is not null)
            .Concat(LinesFor(entry)).ToList();
}
