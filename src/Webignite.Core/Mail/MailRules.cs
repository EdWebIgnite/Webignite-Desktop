namespace Webignite.Core.Mail;

/// <summary>What part of a mail a rule looks at.</summary>
public enum RuleMatch
{
    /// <summary>The sender's address contains the words, e.g. "accounts@".</summary>
    Sender,

    /// <summary>The sender's domain is this one or under it, e.g. "fnb.co.za" also matches "mail.fnb.co.za".</summary>
    Domain,

    /// <summary>The subject contains the words.</summary>
    Subject,

    /// <summary>One of the To or Cc addresses contains the words, e.g. "accounts@webignite.co.za".</summary>
    To,
}

/// <summary>
/// "Mail from fnb.co.za goes to Banking and gets the Accounts colour." One condition and what to do. Kept on this PC.
/// </summary>
public sealed record MailRule
{
    public required string Id { get; init; }

    /// <summary>The mailbox the rule is for, or null for every mailbox.</summary>
    public string? AccountId { get; init; }

    public RuleMatch Match { get; init; } = RuleMatch.Domain;
    public string Text { get; init; } = "";

    /// <summary>The folder to move matching mail to, by its path on the server, or null to leave it where it is.</summary>
    public string? MoveTo { get; init; }

    /// <summary>The category keyword to add, or null.</summary>
    public string? Category { get; init; }

    public bool MarkImportant { get; init; }
    public bool MarkRead { get; init; }
    public bool On { get; init; } = true;

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

/// <summary>The rules, and per mailbox the newest inbox mail they have already looked at.</summary>
public sealed record MailRuleSettings
{
    public IReadOnlyList<MailRule> Rules { get; init; } = [];

    /// <summary>Per mailbox id, the highest inbox UID the rules have seen. New mail is anything above it.</summary>
    public IReadOnlyDictionary<string, uint> RanUpTo { get; init; } = new Dictionary<string, uint>();
}

/// <summary>What the rules want done to one mail. Marks first, the move last, since moving gives the mail a new number.</summary>
public sealed record RuleOutcome(MailSummary Mail, string? MoveTo, IReadOnlyList<string> Categories, bool MarkImportant, bool MarkRead)
{
    public bool DoesAnything => MoveTo is not null || Categories.Count > 0 || MarkImportant || MarkRead;
}

public static class MailRules
{
    /// <summary>What is wrong with a rule before it can be saved, in plain English. Empty means fine.</summary>
    public static IReadOnlyList<string> Check(MailRule rule)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(rule.Text))
            problems.Add(rule.Match switch
            {
                RuleMatch.Domain => "Type the domain, e.g. fnb.co.za.",
                RuleMatch.Subject => "Type the words to look for in the subject.",
                _ => "Type the address or part of it.",
            });
        if (rule.MoveTo is null && rule.Category is null && !rule.MarkImportant && !rule.MarkRead)
            problems.Add("Pick at least one thing to do: move it, give it a category, mark it Important or mark it read.");
        if (rule.MoveTo is not null && rule.AccountId is null)
            problems.Add("Moving needs one mailbox, since each mailbox has its own folders.");
        return problems;
    }

    public static bool Matches(MailRule rule, MailSummary mail)
    {
        if (!rule.On || string.IsNullOrWhiteSpace(rule.Text) || (rule.AccountId is not null && rule.AccountId != mail.AccountId))
            return false;
        var text = rule.Text.Trim();
        var from = mail.From?.Address ?? "";
        return rule.Match switch
        {
            RuleMatch.Sender => from.Contains(text, StringComparison.OrdinalIgnoreCase),
            RuleMatch.Domain => DomainOf(from) is { Length: > 0 } domain
                && (domain.Equals(text.TrimStart('@'), StringComparison.OrdinalIgnoreCase)
                    || domain.EndsWith("." + text.TrimStart('@'), StringComparison.OrdinalIgnoreCase)),
            RuleMatch.Subject => mail.Subject.Contains(text, StringComparison.OrdinalIgnoreCase),
            RuleMatch.To => mail.To.Concat(mail.Cc).Any(p => p.Address.Contains(text, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    /// <summary>
    /// What every rule that matches wants done to each mail, taken together: categories add up, Important and read
    /// add up, and the first matching rule that moves decides where the mail goes. Mail no rule touches is left out.
    /// </summary>
    public static IReadOnlyList<RuleOutcome> Apply(IEnumerable<MailRule> rules, IEnumerable<MailSummary> mail)
    {
        var list = rules.Where(r => r.On).ToList();
        var outcomes = new List<RuleOutcome>();
        foreach (var m in mail)
        {
            var hits = list.Where(r => Matches(r, m)).ToList();
            if (hits.Count == 0)
                continue;
            var outcome = new RuleOutcome(m,
                hits.Select(r => r.MoveTo).FirstOrDefault(p => p is not null && p != m.Folder),
                hits.Select(r => r.Category).OfType<string>().Distinct().Where(k => !MailCategories.Has(m, k)).ToList(),
                hits.Any(r => r.MarkImportant) && !m.Flagged,
                hits.Any(r => r.MarkRead) && !m.Seen);
            if (outcome.DoesAnything)
                outcomes.Add(outcome);
        }
        return outcomes;
    }

    /// <summary>
    /// Inbox mail that arrived since the rules last looked. The first time a mailbox is seen nothing is picked,
    /// so turning rules on doesn't rework the whole inbox; "Run on this folder" does that on purpose.
    /// </summary>
    public static (IReadOnlyList<MailSummary> Fresh, uint RanUpTo) Arrived(IReadOnlyList<MailSummary> inbox, uint? ranUpTo)
    {
        var highest = inbox.Count == 0 ? 0 : inbox.Max(m => m.Uid);
        if (ranUpTo is not { } mark)
            return ([], highest);
        return (inbox.Where(m => m.Uid > mark).ToList(), Math.Max(mark, highest));
    }

    /// <summary>
    /// The rule in a sentence: "Mail from fnb.co.za goes to Banking, gets Accounts and is marked read."
    /// <paramref name="folderName"/> and <paramref name="categoryName"/> turn a path and keyword into what Eddie sees.
    /// </summary>
    public static string Describe(MailRule rule, Func<string, string> folderName, Func<string, string> categoryName)
    {
        var text = rule.Text.Trim();
        var who = rule.Match switch
        {
            RuleMatch.Domain => $"Mail from {text.TrimStart('@')}",
            RuleMatch.Sender => $"Mail from an address with \"{text}\"",
            RuleMatch.Subject => $"Mail with \"{text}\" in the subject",
            _ => $"Mail sent to \"{text}\"",
        };
        var actions = new List<string>();
        if (rule.MoveTo is { } path)
            actions.Add("goes to " + folderName(path));
        if (rule.Category is { } keyword)
            actions.Add("gets " + categoryName(keyword));
        if (rule.MarkImportant)
            actions.Add("is marked Important");
        if (rule.MarkRead)
            actions.Add("is marked read");
        var said = actions.Count switch
        {
            0 => "is left alone",
            1 => actions[0],
            _ => string.Join(", ", actions.Take(actions.Count - 1)) + " and " + actions[^1],
        };
        return $"{who} {said}.";
    }

    private static string DomainOf(string address) => address.LastIndexOf('@') is var at and >= 0 ? address[(at + 1)..].Trim() : "";
}
