namespace Webignite.Core.Mail;

public enum FolderRole
{
    Inbox,
    Drafts,
    Sent,
    Archive,
    Junk,
    Trash,
    Other,
}

public sealed record MailFolder
{
    public required string AccountId { get; init; }

    /// <summary>The server's full name for the folder, e.g. "INBOX" or "INBOX.Sent".</summary>
    public required string Path { get; init; }

    public required string Name { get; init; }
    public FolderRole Role { get; init; } = FolderRole.Other;
    public int Unread { get; init; }
    public int Total { get; init; }

    /// <summary>Changes when the server renumbers the folder; cached mail is then dropped and fetched again.</summary>
    public uint UidValidity { get; init; }

    public DateTimeOffset? Synced { get; init; }
}

public sealed record MailPerson(string? Name, string Address)
{
    public string Display => string.IsNullOrWhiteSpace(Name) ? Address : Name.Trim();
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Address : $"{Name.Trim()} <{Address}>";
}

/// <summary>What the message list needs to know about one mail, without its body.</summary>
public sealed record MailSummary
{
    public required string AccountId { get; init; }
    public required string Folder { get; init; }
    public required uint Uid { get; init; }
    public string? MessageId { get; init; }
    public string? InReplyTo { get; init; }
    public DateTimeOffset Date { get; init; }
    public MailPerson? From { get; init; }
    public IReadOnlyList<MailPerson> To { get; init; } = [];
    public IReadOnlyList<MailPerson> Cc { get; init; } = [];
    public string Subject { get; init; } = "";
    public string Preview { get; init; } = "";
    public bool Seen { get; init; }
    public bool Flagged { get; init; }
    public bool Answered { get; init; }
    public bool HasAttachments { get; init; }
    public long Size { get; init; }
}

public sealed record MailAttachment(string FileName, string ContentType, long Size, int Index);

/// <summary>A downloaded message, ready to show: sanitised HTML or plain text, and its attachments.</summary>
public sealed record MailBody
{
    public string? Html { get; init; }
    public string Text { get; init; } = "";
    public IReadOnlyList<MailAttachment> Attachments { get; init; } = [];

    /// <summary>Whether the HTML asks for pictures from the internet, which stay blocked until you allow them.</summary>
    public bool HasRemoteImages { get; init; }
}
