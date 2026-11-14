namespace Webignite.Core.Studio;

/// <summary>
/// One client on Studio's wall: its name and initials, its colour, how things stand, and on the back what is
/// being done for it.
/// </summary>
public sealed record WallCard(Star Client, string Initials, string Colour, string Status, string Site, IReadOnlyList<Star> Busy, int More);

/// <summary>
/// Studio's wall: a card for every client, the ones with most done for them first. It is made from the same dots
/// as the map, so the two always agree.
/// </summary>
public static class StudioWall
{
    /// <summary>How many things the back of a card lists before it says "and 3 more".</summary>
    public const int Listed = 4;

    private static readonly string[] Colours =
        ["ProjectBlue", "ProjectAqua", "ProjectPurple", "ProjectMagenta", "ProjectBlueGreen", "ProjectIndigo", "ProjectYellowOrange", "ProjectPink", "ProjectGreen"];

    /// <summary>A client's colour, as a palette name: always the same for the same client, on the map and on the wall.</summary>
    public static string ColourOf(string clientId)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in clientId)
                hash = hash * 31 + c;
            return Colours[(int)((uint)hash % Colours.Length)];
        }
    }

    /// <summary>"BB" for Bosveld Builders, "G3" for G3, "K" for Karoo: the first letters of the first two words, or the first two characters of one short word.</summary>
    public static string Initials(string name)
    {
        var words = name.Split([' ', '-', '.', ',', '(', ')', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => char.IsLetterOrDigit(w[0])).ToList();
        if (words.Count == 0)
            return "?";
        if (words.Count == 1)
            return words[0].Length <= 3 ? words[0].ToUpperInvariant() : words[0][..1].ToUpperInvariant();
        return string.Concat(words[0][..1], words[1][..1]).ToUpperInvariant();
    }

    public static string StatusOf(StarState state) => state switch
    {
        StarState.Problem => "Needs attention",
        StarState.Busy => "Work under way",
        _ => "All quiet",
    };

    /// <summary>The cards, heaviest client first. What needs attention is listed first on a card's back, then work under way.</summary>
    public static IReadOnlyList<WallCard> Build(IReadOnlyList<Star> stars) =>
    [
        .. stars.Where(s => s.ParentId is null).OrderByDescending(s => s.Weight).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(client =>
        {
            var around = stars.Where(s => s.ParentId == client.Id).OrderByDescending(s => s.State).ThenBy(s => s.Kind).ToList();
            return new WallCard(client, Initials(client.Name), ColourOf(client.Id), StatusOf(client.State),
                around.FirstOrDefault(s => s.Kind == StarKind.Site)?.Name ?? "", [.. around.Take(Listed)], Math.Max(0, around.Count - Listed));
        }),
    ];
}
