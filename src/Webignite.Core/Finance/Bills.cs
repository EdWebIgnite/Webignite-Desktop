using Webignite.Core.Documents;

namespace Webignite.Core.Finance;

/// <summary>A supplier's bill in Xero: what Webignite owes. Read from Xero only.</summary>
public sealed record XeroBill
{
    public required string Id { get; init; }
    public string? Number { get; init; }
    public required string Supplier { get; init; }
    public string? Reference { get; init; }
    public DateOnly? Date { get; init; }
    public DateOnly? Due { get; init; }
    public decimal Total { get; init; }
    public decimal AmountDue { get; init; }

    /// <summary>DRAFT or SUBMITTED (waiting to be approved in Xero), or AUTHORISED (to pay).</summary>
    public required string Status { get; init; }
}

/// <summary>The open bills as last read from Xero (setting <c>xero-bills</c>).</summary>
public sealed record BillsState
{
    public DateTimeOffset? Read { get; init; }
    public IReadOnlyList<XeroBill> Open { get; init; } = [];
}

/// <summary>What Webignite owes suppliers, from the bills in Xero.</summary>
public static class Bills
{
    /// <summary>Approved and not yet fully paid.</summary>
    public static bool ToPay(XeroBill b) => b.Status == XeroStatuses.Authorised && b.AmountDue > 0;

    /// <summary>Still a draft in Xero, so not approved to pay yet.</summary>
    public static bool AwaitingApproval(XeroBill b) => XeroStatuses.IsDraft(b.Status);

    public static bool IsOverdue(XeroBill b, DateOnly today) => ToPay(b) && b.Due is { } due && due < today;

    public static decimal Owed(IEnumerable<XeroBill> bills) => bills.Where(ToPay).Sum(b => b.AmountDue);

    public static decimal Overdue(IEnumerable<XeroBill> bills, DateOnly today) => bills.Where(b => IsOverdue(b, today)).Sum(b => b.AmountDue);

    /// <summary>To pay within the next <paramref name="days"/> days, overdue ones included.</summary>
    public static decimal DueWithin(IEnumerable<XeroBill> bills, DateOnly today, int days) =>
        bills.Where(b => ToPay(b) && (b.Due is not { } due || due <= today.AddDays(days))).Sum(b => b.AmountDue);

    /// <summary>To pay first: overdue, then by due day, then drafts.</summary>
    public static IReadOnlyList<XeroBill> InOrder(IEnumerable<XeroBill> bills) =>
        bills.OrderBy(b => ToPay(b) ? 0 : 1).ThenBy(b => b.Due ?? DateOnly.MaxValue).ThenBy(b => b.Supplier, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>One line for the Money page and Overview.</summary>
    public static string Summary(IReadOnlyList<XeroBill> bills, DateOnly today)
    {
        var toPay = bills.Where(ToPay).ToList();
        var drafts = bills.Count(AwaitingApproval);
        if (toPay.Count == 0 && drafts == 0)
            return "No bills to pay in Xero.";
        var parts = new List<string>();
        if (toPay.Count > 0)
            parts.Add($"{Money.Rand(Owed(toPay))} owed on {toPay.Count} {(toPay.Count == 1 ? "bill" : "bills")}");
        if (Overdue(toPay, today) is > 0 and var late)
            parts.Add($"{Money.Rand(late)} overdue");
        if (drafts > 0)
            parts.Add($"{drafts} waiting to be approved in Xero");
        return string.Join(", ", parts) + ".";
    }
}
