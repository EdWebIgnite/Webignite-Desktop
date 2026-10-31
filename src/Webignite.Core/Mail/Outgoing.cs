using System.Globalization;
using System.Net;
using System.Text;

namespace Webignite.Core.Mail;

/// <summary>
/// A file going out with a mail. It is either a file on this PC, or an attachment carried over from
/// the message being forwarded or the draft being finished (by its position in that message).
/// </summary>
public sealed record OutgoingAttachment(string FileName, long Size, string? FilePath = null, int? SourceIndex = null);

/// <summary>Mail being written. Sending only ever happens when Eddie presses Send.</summary>
public sealed record OutgoingMail
{
    public required string AccountId { get; init; }
    public IReadOnlyList<MailPerson> To { get; init; } = [];
    public IReadOnlyList<MailPerson> Cc { get; init; } = [];
    public IReadOnlyList<MailPerson> Bcc { get; init; } = [];
    public string Subject { get; init; } = "";

    /// <summary>What was typed, signature included. Plain text; the HTML version is made from it.</summary>
    public string Body { get; init; } = "";

    /// <summary>The message being replied to or forwarded, shown under the new text.</summary>
    public Quoted? Quote { get; init; }

    public string? InReplyTo { get; init; }
    public IReadOnlyList<string> References { get; init; } = [];
    public IReadOnlyList<OutgoingAttachment> Attachments { get; init; } = [];

    /// <summary>The draft on the server this replaces, so saving or sending doesn't leave an old copy behind.</summary>
    public uint? DraftUid { get; init; }
}

/// <summary>The earlier message shown under a reply or forward.</summary>
public sealed record Quoted(string Header, string Text, string? Html, bool IsForward);

public static class Recipients
{
    /// <summary>
    /// Reads what was typed in a To, Cc or Bcc box: addresses split by commas or semicolons, with or
    /// without a name, e.g. "Thandi Mokoena &lt;thandi@karoocoffee.co.za&gt;, pieter@bosveld.co.za".
    /// </summary>
    public static (IReadOnlyList<MailPerson> People, IReadOnlyList<string> Bad) Parse(string? text)
    {
        var people = new List<MailPerson>();
        var bad = new List<string>();
        foreach (var part in Split(text ?? ""))
        {
            var item = part.Trim();
            if (item.Length == 0)
                continue;
            string? name = null;
            var address = item;
            var open = item.LastIndexOf('<');
            if (open >= 0 && item.EndsWith('>'))
            {
                name = item[..open].Trim().Trim('"').Trim();
                address = item[(open + 1)..^1].Trim();
            }
            if (Addresses.LooksValid(address))
            {
                if (!people.Any(p => p.Address.Equals(address, StringComparison.OrdinalIgnoreCase)))
                    people.Add(new MailPerson(string.IsNullOrWhiteSpace(name) ? null : name, address));
            }
            else
                bad.Add(item);
        }
        return (people, bad);
    }

    public static string Format(IEnumerable<MailPerson> people) => string.Join(", ", people.Select(p => p.ToString()));

    // Splits on commas and semicolons, except inside quotes or angle brackets ("Mokoena, Thandi" <t@k.co.za>).
    private static IEnumerable<string> Split(string text)
    {
        var current = new StringBuilder();
        var quoted = false;
        var angled = false;
        foreach (var c in text)
        {
            if (c == '"') quoted = !quoted;
            else if (c == '<') angled = true;
            else if (c == '>') angled = false;
            if ((c == ',' || c == ';' || c == '\n') && !quoted && !angled)
            {
                yield return current.ToString();
                current.Clear();
            }
            else
                current.Append(c);
        }
        yield return current.ToString();
    }
}

public static class Replies
{
    private static readonly CultureInfo SouthAfrica = CultureInfo.GetCultureInfo("en-ZA");

    /// <summary>
    /// Who a reply goes to. A reply goes to the Reply-To address if the sender set one, otherwise the
    /// sender. Reply all adds everyone else on the mail, leaving out your own mailboxes.
    /// </summary>
    public static (IReadOnlyList<MailPerson> To, IReadOnlyList<MailPerson> Cc) Recipients(
        MailPerson? from, IReadOnlyList<MailPerson> replyTo, IReadOnlyList<MailPerson> to, IReadOnlyList<MailPerson> cc,
        IReadOnlyCollection<string> myAddresses, bool all)
    {
        bool Mine(MailPerson p) => myAddresses.Contains(p.Address, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<MailPerson> Keep(IEnumerable<MailPerson> people) => people.Where(p => !Mine(p) && seen.Add(p.Address)).ToList();

        var main = replyTo.Count > 0 ? replyTo : from is null ? [] : [from];
        // Replying to your own sent mail goes back to the people you wrote to.
        if (main.All(Mine))
            main = to;
        var first = Keep(main);
        if (!all)
            return (first, []);
        var others = Keep(to.Concat(cc));
        return (first, others);
    }

    public static string Header(MailPerson? from, DateTimeOffset date) =>
        $"On {date.ToLocalTime().ToString("dddd, d MMMM yyyy 'at' HH:mm", SouthAfrica)}, {from?.Display ?? "someone"} wrote:";

    public static string ForwardHeader(MailPerson? from, IReadOnlyList<MailPerson> to, DateTimeOffset date, string subject) =>
        "---------- Forwarded message ----------\n"
        + $"From: {from}\n"
        + $"Date: {date.ToLocalTime().ToString("dddd, d MMMM yyyy 'at' HH:mm", SouthAfrica)}\n"
        + $"Subject: {subject}\n"
        + $"To: {string.Join(", ", to.Select(p => p.ToString()))}";

    /// <summary>The thread a reply belongs to: the original's references plus the original itself.</summary>
    public static IReadOnlyList<string> References(IEnumerable<string> originalReferences, string? originalId) =>
        originalReferences.Concat(originalId is null ? [] : [originalId]).Distinct().TakeLast(20).ToList();

    /// <summary>What a new mail starts with: blank lines to type in, then the signature.</summary>
    public static string StartBody(string? signature) =>
        string.IsNullOrWhiteSpace(signature) ? "" : "\n\n" + signature.TrimEnd();

    /// <summary>The plain-text version of a mail: the new text, then the earlier message quoted.</summary>
    public static string TextPart(OutgoingMail mail)
    {
        var body = mail.Body.TrimEnd();
        if (mail.Quote is not { } quote)
            return body;
        var earlier = quote.IsForward
            ? quote.Text.TrimEnd()
            : string.Join('\n', quote.Text.TrimEnd().Replace("\r", "").Split('\n').Select(l => l.Length == 0 ? ">" : "> " + l));
        return body + "\n\n" + quote.Header + "\n" + earlier + "\n";
    }

    /// <summary>The HTML version: the new text, then the earlier message in a quote block.</summary>
    public static string HtmlPart(OutgoingMail mail)
    {
        var html = new StringBuilder("<div style=\"font-family: Arial, sans-serif; font-size: 10.5pt\">");
        html.Append(MailHtml.FromText(mail.Body.TrimEnd()));
        if (mail.Quote is { } quote)
        {
            html.Append("<br><div>").Append(WebUtility.HtmlEncode(quote.Header).Replace("\n", "<br>")).Append("</div>");
            html.Append(quote.IsForward ? "<div>" : "<blockquote style=\"margin:0 0 0 .8ex; border-left:1px solid #ccc; padding-left:1ex\">");
            html.Append(quote.Html ?? MailHtml.FromText(quote.Text));
            html.Append(quote.IsForward ? "</div>" : "</blockquote>");
        }
        return html.Append("</div>").ToString();
    }
}

public static class OutgoingRules
{
    /// <summary>xneelo and most providers refuse mail much over 25 MB, and attachments grow by a third when sent.</summary>
    public const long MaxAttachmentBytes = 18 * 1024 * 1024;

    /// <summary>Problems that stop a mail being sent. Drafts can be saved with none of this filled in.</summary>
    public static IReadOnlyList<string> CheckForSending(OutgoingMail mail, IReadOnlyList<string> badAddresses)
    {
        var problems = new List<string>();
        if (badAddresses.Count > 0)
            problems.Add($"These don't look like email addresses: {string.Join(", ", badAddresses)}");
        if (mail.To.Count + mail.Cc.Count + mail.Bcc.Count == 0)
            problems.Add("Add at least one person to send it to.");
        if (mail.Attachments.Sum(a => a.Size) > MaxAttachmentBytes)
            problems.Add("The attachments are too big to email together (18 MB at most). Send fewer, or share a link instead.");
        return problems;
    }

    /// <summary>A gentle check before sending, like Thunderbird's: mail that mentions an attachment but has none.</summary>
    public static bool MentionsMissingAttachment(OutgoingMail mail)
    {
        if (mail.Attachments.Count > 0)
            return false;
        var text = mail.Body.ToLowerInvariant();
        return new[] { "attached", "attachment", "see attach", "find attach", "enclosed" }.Any(text.Contains);
    }
}
