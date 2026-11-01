using System.Globalization;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Core.Mail;
using Webignite.Core.Renewals;

namespace Webignite.Core.Overview;

/// <summary>Which page a summary line opens.</summary>
public enum SummaryTarget { Invoices, Approvals, Mail, Renewals, Money, Settings }

/// <summary>One line of the morning summary. A problem shows in red; the rest are plain.</summary>
public sealed record SummaryLine(string Text, SummaryTarget Target, bool Problem = false, MailSummary? Mail = null);

/// <summary>Everything the summary looks at, read by the app from the database.</summary>
public sealed record SummaryInput
{
    public required DateOnly Today { get; init; }
    public IReadOnlyList<StoredDocument> Documents { get; init; } = [];
    public IReadOnlyList<Renewal> Renewals { get; init; } = [];
    public IReadOnlyList<MoneyEntry> Entries { get; init; } = [];
    public IReadOnlyList<MailSummary> Inbox { get; init; } = [];
    public IReadOnlyList<Client> Clients { get; init; } = [];
    public IReadOnlyCollection<string> OwnAddresses { get; init; } = [];
    public bool BackupsOn { get; init; } = true;
    public bool BackupOverdue { get; init; }
}

/// <summary>
/// What needs Eddie today, in one place: money in, overdue invoices, things to approve, clients waiting on a
/// reply, renewals coming up and backups. Lines that need doing come first.
/// </summary>
public static class MorningSummary
{
    /// <summary>A client mail counts as waiting for this long; older ones are left to the inbox.</summary>
    public const int ReplyDays = 14;

    public const int RenewalDays = 14;

    // Shared mail services: matching on these domains would make every Gmail sender a client.
    private static readonly HashSet<string> SharedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "yahoo.com", "icloud.com", "me.com",
        "mweb.co.za", "telkomsa.net", "vodamail.co.za", "webmail.co.za", "iafrica.com", "absamail.co.za", "lantic.net",
    };

    public static IReadOnlyList<SummaryLine> Build(SummaryInput input)
    {
        var lines = new List<SummaryLine>();
        var today = input.Today;

        if (!input.BackupsOn)
            lines.Add(new("Backups are off. Choose a backup password in Settings.", SummaryTarget.Settings, Problem: true));
        else if (input.BackupOverdue)
            lines.Add(new("The daily backup hasn't run for more than two days. Check Settings.", SummaryTarget.Settings, Problem: true));

        var overdue = input.Documents
            .Where(d => d.Status == DocumentStatus.InXero && d.Document.Kind == DocumentKind.Invoice && d.Document.DueOrExpiryDate < today)
            .ToList();
        if (overdue.Count > 0)
        {
            var amount = Money.Rand(overdue.Sum(d => d.Document.Total ?? 0));
            lines.Add(new(overdue.Count == 1
                ? $"1 invoice is overdue ({overdue[0].Document.Customer.Name}, {amount})."
                : $"{overdue.Count} invoices are overdue, {amount} in all.", SummaryTarget.Invoices, Problem: true));
        }

        foreach (var renewal in input.Renewals.Where(r => r.RenewsOn < today).OrderBy(r => r.RenewsOn))
            lines.Add(new($"{renewal.Name} was due for renewal {On(renewal.RenewsOn, today)}.", SummaryTarget.Renewals, Problem: true));

        var waiting = Approvals.Waiting(input.Documents).Count;
        if (waiting > 0)
            lines.Add(new(waiting == 1 ? "1 quote or invoice is waiting for your approval." : $"{waiting} quotes and invoices are waiting for your approval.",
                SummaryTarget.Approvals));

        lines.AddRange(WaitingOnReply(input));

        foreach (var renewal in input.Renewals.Where(r => r.RenewsOn >= today && r.RenewsOn <= today.AddDays(RenewalDays)).OrderBy(r => r.RenewsOn))
        {
            var who = renewal.ClientName is { Length: > 0 } client ? $" for {client}" : "";
            lines.Add(new($"{renewal.Name}{who} renews {On(renewal.RenewsOn, today)}.", SummaryTarget.Renewals));
        }

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var received = input.Entries.Where(e => e.Kind == EntryKind.Received && e.Date >= monthStart && e.Date <= today).Sum(e => e.Amount);
        if (received > 0)
            lines.Add(new($"{Money.Rand(received)} has come in so far in {today.ToString("MMMM", CultureInfo.InvariantCulture)}.", SummaryTarget.Money));

        return lines;
    }

    /// <summary>
    /// Mail from clients in the last two weeks that hasn't been answered, one line per person, newest first.
    /// A sender counts as a client when the address matches the client's email, or its own domain.
    /// </summary>
    public static IReadOnlyList<SummaryLine> WaitingOnReply(SummaryInput input)
    {
        var since = input.Today.AddDays(-ReplyDays);
        var own = new HashSet<string>(input.OwnAddresses, StringComparer.OrdinalIgnoreCase);
        var lines = new List<SummaryLine>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mail in input.Inbox.Where(m => !m.Answered && m.From is not null && DateOnly.FromDateTime(m.Date.LocalDateTime) >= since)
                     .OrderByDescending(m => m.Date))
        {
            var from = mail.From!;
            if (own.Contains(from.Address) || !seen.Add(from.Address) || ClientFor(from.Address, input.Clients) is not { } client)
                continue;
            var who = string.Equals(from.Display, client.Name, StringComparison.OrdinalIgnoreCase) ? client.Name : $"{from.Display} ({client.Name})";
            lines.Add(new($"{who} is waiting on a reply since {Day(DateOnly.FromDateTime(mail.Date.LocalDateTime), input.Today)}: {mail.Subject}",
                SummaryTarget.Mail, Mail: mail));
        }
        return lines;
    }

    public static Client? ClientFor(string address, IEnumerable<Client> clients)
    {
        var domain = Domain(address);
        Client? byDomain = null;
        foreach (var client in clients)
        {
            if (client.Email is not { Length: > 0 } email)
                continue;
            if (string.Equals(email.Trim(), address, StringComparison.OrdinalIgnoreCase))
                return client;
            var clientDomain = Domain(email);
            if (byDomain is null && domain.Length > 0 && !SharedDomains.Contains(domain) && string.Equals(clientDomain, domain, StringComparison.OrdinalIgnoreCase))
                byDomain = client;
        }
        return byDomain;
    }

    private static string Domain(string address) => address.LastIndexOf('@') is var at and >= 0 ? address[(at + 1)..].Trim() : "";

    /// <summary>"today", "yesterday", "tomorrow", a weekday within a week, otherwise "3 November".</summary>
    public static string Day(DateOnly day, DateOnly today) => (today.DayNumber - day.DayNumber) switch
    {
        0 => "today",
        1 => "yesterday",
        -1 => "tomorrow",
        > -7 and < 7 => day.ToString("dddd", CultureInfo.InvariantCulture),
        _ => day.ToString("d MMMM", CultureInfo.InvariantCulture),
    };

    /// <summary>The same with "on" where English wants it: "on Thursday", but "tomorrow".</summary>
    public static string On(DateOnly day, DateOnly today) =>
        Math.Abs(today.DayNumber - day.DayNumber) <= 1 ? Day(day, today) : "on " + Day(day, today);
}
