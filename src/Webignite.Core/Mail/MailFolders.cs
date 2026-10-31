namespace Webignite.Core.Mail;

public static class MailFolders
{
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

public static class MailSearch
{
    public static bool Matches(MailSummary summary, string word) =>
        Has(summary.Subject, word) || Has(summary.Preview, word)
        || (summary.From is { } from && (Has(from.Name, word) || Has(from.Address, word)))
        || summary.To.Any(p => Has(p.Name, word) || Has(p.Address, word))
        || summary.Cc.Any(p => Has(p.Name, word) || Has(p.Address, word));

    private static bool Has(string? text, string word) => text?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;
}
