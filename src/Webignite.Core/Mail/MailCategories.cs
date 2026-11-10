using System.Text;

namespace Webignite.Core.Mail;

/// <summary>
/// A category you make yourself, such as "Waiting" in purple. It is kept on the mail server as an IMAP keyword,
/// so it follows the mail to your phone and to Thunderbird, which shows keywords as tags.
/// </summary>
/// <param name="Keyword">The word stored on the server. It stays the same when the category is renamed.</param>
/// <param name="Name">What you see.</param>
/// <param name="Colour">One of <see cref="MailCategories.Colours"/>.</param>
public sealed record MailCategory(string Keyword, string Name, string Colour);

public static class MailCategories
{
    /// <summary>
    /// The colours a category or tag can have, as macOS offers them, red and green included: these are colours the
    /// person picks for their own labels, so they don't carry the app's meaning of problem and done.
    /// </summary>
    public static IReadOnlyList<string> Colours { get; } =
        ["red", "orange", "yellow", "lime", "green", "mint", "teal", "cyan", "blue", "indigo", "purple", "magenta", "pink", "brown", "grey", "graphite"];

    /// <summary>The order new categories and tags take colours in, calm ones first, so a new one isn't red by itself.</summary>
    public static IReadOnlyList<string> SuggestedOrder { get; } =
        ["blue", "purple", "orange", "yellow", "teal", "pink", "green", "indigo", "mint", "magenta", "cyan", "lime", "brown", "red", "grey", "graphite"];

    public const int LongestName = 30;

    /// <summary>What is wrong with this name, or null when it can be used.</summary>
    public static string? Check(IReadOnlyList<MailCategory> existing, string name, string? editing = null)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return "Give the category a name.";
        if (trimmed.Length > LongestName)
            return $"Keep the name under {LongestName} letters.";
        if (existing.Any(c => c.Keyword != editing && c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            return "There is already a category with that name.";
        return null;
    }

    /// <summary>Adds a category. A colour that isn't in the list becomes the first one not used yet.</summary>
    public static IReadOnlyList<MailCategory> Add(IReadOnlyList<MailCategory> existing, string name, string? colour = null)
    {
        if (Check(existing, name) is { } problem)
            throw new ArgumentException(problem, nameof(name));
        var trimmed = name.Trim();
        var keyword = KeywordFor(trimmed, existing.Select(c => c.Keyword));
        return [.. existing, new MailCategory(keyword, trimmed, colour is not null && Colours.Contains(colour) ? colour : NextColour(existing))];
    }

    /// <summary>Renames or recolours a category. Mail already in it stays in it, because the keyword doesn't change.</summary>
    public static IReadOnlyList<MailCategory> Change(IReadOnlyList<MailCategory> existing, string keyword, string name, string colour)
    {
        if (Check(existing, name, keyword) is { } problem)
            throw new ArgumentException(problem, nameof(name));
        return existing.Select(c => c.Keyword == keyword
            ? c with { Name = name.Trim(), Colour = Colours.Contains(colour) ? colour : c.Colour }
            : c).ToList();
    }

    /// <summary>Takes a category off the list. Mail keeps the keyword on the server, so adding it back brings the mail back.</summary>
    public static IReadOnlyList<MailCategory> Remove(IReadOnlyList<MailCategory> existing, string keyword) =>
        existing.Where(c => c.Keyword != keyword).ToList();

    public static string NextColour(IReadOnlyList<MailCategory> existing) =>
        SuggestedOrder.FirstOrDefault(colour => existing.All(c => c.Colour != colour)) ?? SuggestedOrder[existing.Count % SuggestedOrder.Count];

    /// <summary>The categories this mail is in, in the order of the list.</summary>
    public static IReadOnlyList<MailCategory> Of(IReadOnlyList<MailCategory> all, MailSummary mail) =>
        mail.Keywords.Count == 0 ? [] : all.Where(c => Has(mail, c.Keyword)).ToList();

    /// <summary>The mail with a keyword added or taken off. Letter case doesn't matter, as on the server.</summary>
    public static MailSummary With(MailSummary mail, string keyword, bool on) => mail with
    {
        Keywords = on
            ? (Has(mail, keyword) ? mail.Keywords : [.. mail.Keywords, keyword])
            : mail.Keywords.Where(k => !k.Equals(keyword, StringComparison.OrdinalIgnoreCase)).ToList(),
    };

    /// <summary>Whether two keyword lists hold the same words, in any order and letter case.</summary>
    public static bool SameKeywords(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b) =>
        a.Count == b.Count && a.All(k => b.Contains(k, StringComparer.OrdinalIgnoreCase));

    public static bool Has(MailSummary mail, string keyword) =>
        mail.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The server word for a name, the way Thunderbird makes them: lower case, spaces as underscores, only
    /// plain letters and digits. "Waiting on client" becomes "waiting_on_client".
    /// </summary>
    public static string KeywordFor(string name, IEnumerable<string> taken)
    {
        var word = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
                word.Append(ch);
            else if ((ch is ' ' or '_') && word.Length > 0 && word[^1] != '_')
                word.Append('_');
        }
        var stem = word.ToString().Trim('_', '-');
        if (stem.Length == 0)
            stem = "category";
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keyword = stem;
        for (var n = 2; used.Contains(keyword); n++)
            keyword = $"{stem}_{n}";
        return keyword;
    }
}

/// <summary>The quick filters above the message list.</summary>
public enum MailFilter
{
    All,
    Unread,
    Important,
    Attachments,
}

public static class MailFilters
{
    public static bool Keep(MailSummary mail, MailFilter filter) => filter switch
    {
        MailFilter.Unread => !mail.Seen,
        MailFilter.Important => mail.Flagged,
        MailFilter.Attachments => mail.HasAttachments,
        _ => true,
    };

    public static string Label(MailFilter filter) => filter switch
    {
        MailFilter.Unread => "Unread",
        MailFilter.Important => "Important",
        MailFilter.Attachments => "Attachments",
        _ => "All",
    };

    /// <summary>What to say when a filter leaves the list empty.</summary>
    public static string Empty(MailFilter filter) => filter switch
    {
        MailFilter.Unread => "Nothing unread here.",
        MailFilter.Important => "Nothing marked important here.",
        MailFilter.Attachments => "No mail with attachments here.",
        _ => "No mail in this folder.",
    };
}
