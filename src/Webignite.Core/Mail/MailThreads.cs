namespace Webignite.Core.Mail;

/// <summary>One conversation in the message list: a first mail and the replies to it, oldest first.</summary>
public sealed record MailThread(IReadOnlyList<MailSummary> Messages)
{
    /// <summary>The newest mail, which the row shows and the reader opens.</summary>
    public MailSummary Latest => Messages[^1];
    public MailSummary First => Messages[0];
    public int Count => Messages.Count;
    public bool AnyUnread => Messages.Any(m => !m.Seen);
}

/// <summary>How the message list looks, kept on this PC.</summary>
public sealed record MailViewSettings
{
    /// <summary>The lists (a folder, All inboxes, Important or a category, by their key) showing one row per mail.</summary>
    public IReadOnlyList<string> ConversationsOff { get; init; } = [];

    public bool ConversationsIn(string list) => !ConversationsOff.Contains(list);

    /// <summary>The conversation above the open mail shows only its heading, for every mail until opened again.</summary>
    public bool ThreadFolded { get; init; }

    /// <summary>The attachments under the mail show only their heading.</summary>
    public bool AttachmentsFolded { get; init; }

    /// <summary>Who else the mail went to (To and Cc) shows under the sender.</summary>
    public bool ShowRecipients { get; init; }

    public MailViewSettings WithConversations(string list, bool on) => this with
    {
        ConversationsOff = on ? ConversationsOff.Where(k => k != list).ToList() : ConversationsOff.Append(list).Distinct().ToList(),
    };
}

/// <summary>
/// Groups mail into conversations, as Apple Mail and Outlook do. A reply joins the mail it answers (its
/// In-Reply-To matches that mail's Message-ID). A mail whose subject starts with Re: or Fwd: also joins an
/// earlier mail in the same mailbox with the same subject, for mail programs that leave In-Reply-To out.
/// Mail with no reply prefix and no In-Reply-To always starts its own conversation, so two separate mails both
/// called "Invoice" stay apart.
/// </summary>
public static class MailThreads
{
    /// <summary>What mail programs put before a reply's subject, in English, German, Dutch and Scandinavian.</summary>
    public static readonly IReadOnlyList<string> ReplyPrefixes = ["re:", "aw:", "sv:", "antw:"];

    /// <summary>What mail programs put before a forwarded subject.</summary>
    public static readonly IReadOnlyList<string> ForwardPrefixes = ["fw:", "fwd:", "wg:"];

    private static readonly string[] Prefixes = [.. ReplyPrefixes, .. ForwardPrefixes];

    /// <summary>The subject without reply and forward prefixes, in lower case, for matching.</summary>
    public static string Topic(string? subject)
    {
        var s = (subject ?? "").Trim();
        while (true)
        {
            var prefix = Prefixes.FirstOrDefault(p => s.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (prefix is null)
                break;
            s = s[prefix.Length..].TrimStart();
        }
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    /// <summary>Whether the subject starts with Re:, Fwd: or the like.</summary>
    public static bool IsReply(string? subject) =>
        Prefixes.Any(p => (subject ?? "").TrimStart().StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>The mail grouped into conversations, newest conversation first. Each keeps its mail oldest first.</summary>
    /// <param name="arrived">When each mail counts as arriving, for the order of the list; its date unless given.</param>
    public static IReadOnlyList<MailThread> Group(IEnumerable<MailSummary> mail, Func<MailSummary, DateTimeOffset>? arrived = null)
    {
        arrived ??= m => m.Date;
        var list = mail.OrderBy(m => m.Date).ToList();
        var parent = Enumerable.Range(0, list.Count).ToArray();
        int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);
        void Join(int a, int b) => parent[Root(b)] = Root(a);

        var byMessageId = new Dictionary<(string, string), int>();
        var byTopic = new Dictionary<(string, string), int>();
        for (var i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (!string.IsNullOrWhiteSpace(m.InReplyTo) && byMessageId.TryGetValue((m.AccountId, m.InReplyTo.Trim()), out var answered))
                Join(answered, i);
            else if (IsReply(m.Subject) && Topic(m.Subject) is { Length: > 0 } topic && byTopic.TryGetValue((m.AccountId, topic), out var same))
                Join(same, i);

            if (!string.IsNullOrWhiteSpace(m.MessageId))
                byMessageId.TryAdd((m.AccountId, m.MessageId.Trim()), i);
            if (Topic(m.Subject) is { Length: > 0 } own)
                byTopic.TryAdd((m.AccountId, own), i);
        }

        return list.Select((m, i) => (Mail: m, Root: Root(i)))
            .GroupBy(x => x.Root)
            .Select(g => new MailThread(g.Select(x => x.Mail).ToList()))
            .OrderByDescending(t => t.Messages.Max(arrived))
            .ToList();
    }

    /// <summary>Each mail as a conversation of its own, for folders with conversations switched off.</summary>
    public static IReadOnlyList<MailThread> Single(IEnumerable<MailSummary> mail, Func<MailSummary, DateTimeOffset>? arrived = null) =>
        mail.OrderByDescending(arrived ?? (m => m.Date)).Select(m => new MailThread([m])).ToList();
}
