namespace Webignite.Core.Mail;

/// <summary>How often mail is checked in the background, and the newest inbox mail already announced per mailbox.</summary>
public sealed record MailCheckSettings
{
    public int Minutes { get; init; } = 5;
    public bool Notify { get; init; } = true;

    /// <summary>Per mailbox id, the highest inbox UID already counted. New mail is anything above it.</summary>
    public IReadOnlyDictionary<string, uint> Announced { get; init; } = new Dictionary<string, uint>();
}

/// <summary>The new mail found in one check, and the marks to save for next time.</summary>
public sealed record NewMailResult(IReadOnlyList<MailSummary> Fresh, IReadOnlyDictionary<string, uint> Announced);

public static class NewMail
{
    public static readonly IReadOnlyList<int> MinuteChoices = [2, 5, 10, 15, 30, 60];

    /// <summary>Keeps the interval to one of the choices, so a bad saved value can't hammer the mail server.</summary>
    public static TimeSpan Interval(MailCheckSettings settings) =>
        TimeSpan.FromMinutes(MinuteChoices.Contains(settings.Minutes) ? settings.Minutes : 5);

    /// <summary>
    /// Unread inbox mail that arrived since the last check. The first time a mailbox is seen nothing is announced,
    /// so setting up a mailbox doesn't announce years of old mail. Mail already read elsewhere isn't announced either.
    /// </summary>
    public static NewMailResult Find(IReadOnlyDictionary<string, uint> announced, IEnumerable<(string AccountId, IReadOnlyList<MailSummary> Inbox)> inboxes)
    {
        var fresh = new List<MailSummary>();
        var marks = new Dictionary<string, uint>();
        foreach (var (accountId, inbox) in inboxes)
        {
            var highest = inbox.Count == 0 ? 0 : inbox.Max(m => m.Uid);
            if (!announced.TryGetValue(accountId, out var mark))
            {
                marks[accountId] = highest;
                continue;
            }
            fresh.AddRange(inbox.Where(m => m.Uid > mark && !m.Seen));
            marks[accountId] = Math.Max(mark, highest);
        }
        return new NewMailResult(fresh.OrderByDescending(m => m.Date).ToList(), marks);
    }

    /// <summary>The note's title and text: who and what for one mail, a count for several.</summary>
    public static (string Title, string Text) Note(IReadOnlyList<MailSummary> fresh)
    {
        if (fresh.Count == 1)
        {
            var mail = fresh[0];
            var from = mail.From is { } person ? (string.IsNullOrWhiteSpace(person.Name) ? person.Address : person.Name) : "Someone";
            return (from, string.IsNullOrWhiteSpace(mail.Subject) ? "(no subject)" : mail.Subject);
        }
        var senders = fresh.Select(m => m.From?.Name is { Length: > 0 } name ? name : m.From?.Address).OfType<string>().Distinct().ToList();
        var text = senders.Count switch
        {
            0 => "",
            <= 3 => "From " + string.Join(", ", senders),
            _ => "From " + string.Join(", ", senders.Take(3)) + " and others",
        };
        return ($"{fresh.Count} new mails", text);
    }
}
