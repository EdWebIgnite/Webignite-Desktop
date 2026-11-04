namespace Webignite.Core.Mail;

public static class MailFolders
{
    /// <summary>
    /// Mail Eddie sent or is writing, where a list shows who it's to rather than who it's from. Decided by the
    /// folder's role, so "Sent Items" and "Sent Messages" count as well as "Sent".
    /// </summary>
    public static bool IsOutgoing(FolderRole? role) => role is FolderRole.Sent or FolderRole.Drafts;

    /// <summary>Inbox first, then drafts, sent, archive, junk and trash, then the rest A to Z.</summary>
    public static IReadOnlyList<MailFolder> Sort(IEnumerable<MailFolder> folders) =>
        folders.OrderBy(f => f.Role).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Works out a folder's job from its name when the server doesn't say. xneelo uses names like
    /// "INBOX.Sent" and "INBOX.Trash"; others use "Sent Items" or "Deleted Items".
    /// </summary>
    public static FolderRole Guess(string path, char separator)
    {
        if (path.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            return FolderRole.Inbox;
        var name = path.Split(separator == '\0' ? '/' : separator).Last().Trim().ToLowerInvariant();
        return name switch
        {
            "drafts" or "draft" => FolderRole.Drafts,
            "sent" or "sent items" or "sent mail" or "sent messages" => FolderRole.Sent,
            "archive" or "archives" => FolderRole.Archive,
            "junk" or "spam" or "junk e-mail" or "junk email" => FolderRole.Junk,
            "trash" or "deleted items" or "deleted messages" or "bin" => FolderRole.Trash,
            _ => FolderRole.Other,
        };
    }

    /// <summary>The name to show: the last part of the path, with the usual names in plain English.</summary>
    public static string DisplayName(string path, char separator, FolderRole role) => role switch
    {
        FolderRole.Inbox => "Inbox",
        FolderRole.Drafts => "Drafts",
        FolderRole.Sent => "Sent",
        FolderRole.Archive => "Archive",
        FolderRole.Junk => "Junk",
        FolderRole.Trash => "Trash",
        _ => path.Split(separator == '\0' ? '/' : separator).Last(),
    };
}

public static class FolderNames
{
    public const int Longest = 40;

    /// <summary>
    /// What is wrong with a name for a folder you make or rename, or null when it is fine. Dots and slashes are
    /// what servers use between folder levels, so they can't be part of a name.
    /// </summary>
    public static string? Check(string name, IEnumerable<string> siblings, string? renaming = null)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return "Give the folder a name.";
        if (trimmed.Length > Longest)
            return $"Keep the name under {Longest} letters.";
        if (trimmed.IndexOfAny(['.', '/', '\\', '*', '%', '"']) >= 0)
            return "A folder name can't have a dot, slash, star, percent sign or quote in it.";
        if (trimmed.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            return "Inbox is taken. Pick another name.";
        if (renaming is not null && trimmed == renaming)
            return null;
        if (siblings.Any(s => s.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            return "There is already a folder with that name here.";
        return null;
    }

    /// <summary>The names of the folders directly inside a folder, e.g. inside "INBOX" for xneelo's "INBOX.Clients".</summary>
    public static IReadOnlyList<string> Children(IEnumerable<MailFolder> folders, string parent) =>
        folders.Where(f => f.Path.Length > parent.Length + 1
                && f.Path.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                && f.Path[parent.Length] is '.' or '/'
                && f.Path.IndexOfAny(['.', '/'], parent.Length + 1) < 0)
            .Select(f => f.Name)
            .ToList();

    /// <summary>The folder a folder sits in: "INBOX" for "INBOX.Clients", "" for a top-level one.</summary>
    public static string Parent(string path)
    {
        var cut = path.LastIndexOfAny(['.', '/']);
        return cut < 0 ? "" : path[..cut];
    }

    /// <summary>Only your own folders can be renamed or removed. Inbox, Sent, Drafts, Archive, Junk and Trash stay.</summary>
    public static bool CanChange(MailFolder folder) => folder.Role == FolderRole.Other;

    /// <summary>The Archive folder for one mailbox, if it has one.</summary>
    public static MailFolder? Archive(IEnumerable<MailFolder> folders) => folders.FirstOrDefault(f => f.Role == FolderRole.Archive);

    /// <summary>Folders a message can be moved to from where it is now: every other folder except Drafts.</summary>
    public static IReadOnlyList<MailFolder> MoveTargets(IEnumerable<MailFolder> folders, string from) =>
        folders.Where(f => f.Path != from && f.Role != FolderRole.Drafts).ToList();
}

public static class MailSearch
{
    public static bool Matches(MailSummary summary, string word) =>
        Has(summary.Subject, word) || Has(summary.Preview, word)
        || (summary.From is { } from && (Has(from.Name, word) || Has(from.Address, word)))
        || summary.To.Any(p => Has(p.Name, word) || Has(p.Address, word))
        || summary.Cc.Any(p => Has(p.Name, word) || Has(p.Address, word));

    private static bool Has(string? text, string word) => text?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;
}
