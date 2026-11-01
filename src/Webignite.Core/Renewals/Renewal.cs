using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Core.Tasks;

namespace Webignite.Core.Renewals;

public enum RenewalKind
{
    Domain,
    Hosting,
    Ssl,
    Other,
}

/// <summary>A domain, hosting plan, certificate or anything else that has to be renewed on a date.</summary>
public sealed record Renewal
{
    public required string Id { get; init; }

    /// <summary>The domain or a short name, e.g. karoocoffee.co.za or "Bosveld hosting".</summary>
    public required string Name { get; init; }

    public RenewalKind Kind { get; init; } = RenewalKind.Domain;

    /// <summary>The client it belongs to, or null for Webignite's own.</summary>
    public string? ClientName { get; init; }

    /// <summary>Who it is renewed with, e.g. xneelo or domains.co.za.</summary>
    public string? Provider { get; init; }

    public required DateOnly RenewsOn { get; init; }

    /// <summary>How long one renewal lasts. 12 for yearly, 1 for monthly.</summary>
    public int PeriodMonths { get; init; } = 12;

    /// <summary>What Webignite pays the provider, if known.</summary>
    public decimal? Cost { get; init; }

    /// <summary>What the client is charged. When set, an invoice is drafted before the renewal date.</summary>
    public decimal? Price { get; init; }

    public int RemindDaysBefore { get; init; } = 30;
    public string? Notes { get; init; }
    public DateTimeOffset Created { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

public enum RenewalState
{
    Fine,
    DueSoon,
    Overdue,
}

public static class RenewalRules
{
    public static string KindLabel(RenewalKind kind) => kind switch
    {
        RenewalKind.Domain => "Domain",
        RenewalKind.Hosting => "Hosting",
        RenewalKind.Ssl => "SSL certificate",
        _ => "Renewal",
    };

    public static RenewalState State(Renewal renewal, DateOnly today) =>
        renewal.RenewsOn < today ? RenewalState.Overdue
        : renewal.RenewsOn <= today.AddDays(renewal.RemindDaysBefore) ? RenewalState.DueSoon
        : RenewalState.Fine;

    /// <summary>The last day the renewal covers, e.g. 14 Nov 2026 to 13 Nov 2027.</summary>
    public static DateOnly CoversUntil(Renewal renewal) => renewal.RenewsOn.AddMonths(renewal.PeriodMonths).AddDays(-1);

    public static string TaskKey(Renewal renewal) => $"renewal:{renewal.Id}:{renewal.RenewsOn:yyyy-MM-dd}";
    public static string InvoiceKey(Renewal renewal) => $"renewal-invoice:{renewal.Id}:{renewal.RenewsOn:yyyy-MM-dd}";

    /// <summary>A reminder to renew, due on the renewal date, once it is within the reminder window.</summary>
    public static IReadOnlyList<TaskItem> Reminders(IEnumerable<Renewal> renewals, IReadOnlySet<string> knownKeys, DateOnly today, DateTimeOffset now) =>
        renewals
            .Where(r => State(r, today) != RenewalState.Fine && !knownKeys.Contains(TaskKey(r)))
            .Select(r => new TaskItem
            {
                Id = TaskItem.NewId(),
                Title = $"Renew {r.Name}{(string.IsNullOrWhiteSpace(r.ClientName) ? "" : $" for {r.ClientName.Trim()}")}",
                Due = r.RenewsOn,
                Source = TaskSource.Auto,
                Customer = string.IsNullOrWhiteSpace(r.ClientName) ? null : r.ClientName.Trim(),
                AutoKey = TaskKey(r),
                Created = now,
            })
            .ToList();

    /// <summary>The invoice for one renewal period, dated today so it reaches the client before the renewal.</summary>
    public static Document InvoiceFor(Renewal renewal, DocumentSettings settings, DateOnly today) =>
        DocumentDefaults.Complete(new Document
        {
            Kind = DocumentKind.Invoice,
            Origin = DocumentOrigin.New,
            Customer = new Customer { Name = renewal.ClientName!.Trim() },
            Reference = $"{KindLabel(renewal.Kind)} renewal {renewal.Name.Trim()}",
            Lines =
            [
                new Line
                {
                    Description = $"{KindLabel(renewal.Kind)} renewal: {renewal.Name.Trim()}, {renewal.RenewsOn:d MMM yyyy} to {CoversUntil(renewal):d MMM yyyy}",
                    Quantity = 1,
                    UnitPrice = renewal.Price,
                },
            ],
        }, settings, today);

    /// <summary>
    /// Renewals the client pays for, inside the reminder window and not invoiced yet for this date,
    /// as (key, draft invoice) pairs.
    /// </summary>
    public static IReadOnlyList<(string Key, Document Invoice)> InvoicesDue(IEnumerable<Renewal> renewals, IReadOnlySet<string> drafted,
        DocumentSettings settings, DateOnly today) =>
        renewals
            .Where(r => r.Price > 0 && !string.IsNullOrWhiteSpace(r.ClientName))
            .Where(r => State(r, today) != RenewalState.Fine && !drafted.Contains(InvoiceKey(r)))
            .Select(r => (InvoiceKey(r), InvoiceFor(r, settings, today)))
            .ToList();

    /// <summary>
    /// Marks a renewal done: it moves on by one period, and if it cost something an expense is
    /// returned to add under Money.
    /// </summary>
    public static (Renewal Next, MoneyEntry? Expense) Renewed(Renewal renewal, DateOnly paidOn, DateTimeOffset now) =>
        (renewal with { RenewsOn = renewal.RenewsOn.AddMonths(renewal.PeriodMonths) },
         renewal.Cost is > 0 and var cost
            ? new MoneyEntry
            {
                Id = MoneyEntry.NewId(),
                Kind = EntryKind.Expense,
                Date = paidOn,
                Amount = cost,
                Party = string.IsNullOrWhiteSpace(renewal.Provider) ? renewal.Name.Trim() : renewal.Provider.Trim(),
                Category = renewal.Kind == RenewalKind.Domain ? "Domains" : "Hosting and servers",
                Note = $"{KindLabel(renewal.Kind)} renewal: {renewal.Name.Trim()}",
                Created = now,
            }
            : null);

    public static IReadOnlyList<string> Check(Renewal renewal)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(renewal.Name))
            problems.Add("Give it a name, like the domain.");
        if (renewal.PeriodMonths is < 1 or > 120)
            problems.Add("Choose how often it renews.");
        if (renewal.Cost < 0 || renewal.Price < 0)
            problems.Add("Amounts can't be less than R 0.");
        if (renewal.Price > 0 && ClientDirectory.Key(renewal.ClientName).Length == 0)
            problems.Add("Choose the client to charge, or leave the charge empty.");
        if (renewal.RemindDaysBefore is < 0 or > 365)
            problems.Add("Remind between 0 and 365 days before.");
        return problems;
    }
}
