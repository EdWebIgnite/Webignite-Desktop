namespace Webignite.Core.Documents;

public enum ChaseKind { FollowUp, Reminder }

/// <summary>A quote to follow up or an overdue invoice to remind about.</summary>
public sealed record ChaseItem(StoredDocument Stored, ChaseKind Kind, int Days, bool Due)
{
    public string Number => Stored.XeroNumber ?? (Kind == ChaseKind.FollowUp ? "the quote" : "the invoice");
    public string Customer => Stored.Document.Customer.Name;
}

/// <summary>
/// When to chase. A quote emailed from Webignite gets one follow-up after a week if nothing came of it.
/// An overdue invoice gets a reminder, then another each week, until Xero says it's paid.
/// Nothing is sent by itself: Eddie is shown the drafted mail and sends it.
/// </summary>
public static class Chasers
{
    public const int FollowUpAfterDays = 7;
    public const int RemindEveryDays = 7;

    public static IReadOnlyList<ChaseItem> Find(IEnumerable<StoredDocument> documents, DateOnly today)
    {
        var items = new List<ChaseItem>();
        foreach (var stored in documents.Where(d => d.Settled is null && d.XeroId is not null))
        {
            var doc = stored.Document;
            if (doc.Kind == DocumentKind.Quote)
            {
                if (stored.Emailed is not { } sent || stored.Chased is not null || doc.DueOrExpiryDate < today)
                    continue;
                var days = today.DayNumber - Day(sent).DayNumber;
                if (days >= FollowUpAfterDays)
                    items.Add(new ChaseItem(stored, ChaseKind.FollowUp, days, Due: true));
            }
            else if (stored.Status == DocumentStatus.InXero && doc.DueOrExpiryDate is { } due && due < today)
            {
                var overdue = today.DayNumber - due.DayNumber;
                var remind = stored.Chased is not { } last || today.DayNumber - Day(last).DayNumber >= RemindEveryDays;
                items.Add(new ChaseItem(stored, ChaseKind.Reminder, overdue, remind));
            }
        }
        return items.OrderByDescending(i => i.Due).ThenByDescending(i => i.Days).ToList();
    }

    /// <summary>What Xero's status means for chasing: null to go ahead, or why there's nothing to chase.</summary>
    public static string? SettledBy(DocumentKind kind, string xeroStatus, decimal? amountDue) => kind == DocumentKind.Quote
        ? xeroStatus switch
        {
            "ACCEPTED" => "accepted",
            "DECLINED" => "declined",
            "INVOICED" => "invoiced",
            "DELETED" => "deleted",
            _ => null,
        }
        : xeroStatus switch
        {
            "PAID" => "paid",
            "VOIDED" => "voided",
            "DELETED" => "deleted",
            _ when amountDue is 0 => "paid",
            _ => null,
        };

    private static DateOnly Day(DateTimeOffset when) => DateOnly.FromDateTime(when.ToLocalTime().DateTime);
}
