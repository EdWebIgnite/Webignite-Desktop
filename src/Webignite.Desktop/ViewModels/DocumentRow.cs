using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

/// <summary>One line in the quotes or invoices list.</summary>
public sealed record DocumentRow(StoredDocument Stored, DateOnly Today)
{
    public string Number => Stored.XeroNumber ?? "Not in Xero yet";
    public string Customer => string.IsNullOrWhiteSpace(Stored.Document.Customer.Name) ? "No customer yet" : Stored.Document.Customer.Name;
    public string CustomerName => Stored.Document.Customer.Name ?? "";
    public string Date => Stored.Document.Date?.ToString("d MMM yyyy") ?? "";
    public string Total => Money.Rand(Stored.Document.Total ?? 0);

    /// <summary>The quiet second line: reference, and when it was paid or is due.</summary>
    public string Detail => DocumentHeadline.Detail(Stored, Today);
    public bool HasDetail => Detail.Length > 0;

    /// <summary>Waiting for you to approve it: an orange dot.</summary>
    public bool IsWaiting => Stored.Status == DocumentStatus.AwaitingApproval;

    public string Status => Stored.Status switch
    {
        DocumentStatus.Draft => "Draft",
        DocumentStatus.AwaitingApproval => "To approve",
        DocumentStatus.InXero when IsOverdue => "Overdue",
        DocumentStatus.InXero => Receivables.Standing(Stored) ?? "In Xero",
        _ => Stored.Status.ToString(),
    };

    /// <summary>Paid or accepted, or in Xero before Xero has been read for payments: shown in green.</summary>
    public bool IsDone => Stored.SettledAs is "paid" or "accepted" or "invoiced"
        || (IsInXero && Stored.XeroChecked is null && !IsOverdue);

    /// <summary>Past its due date with money still owed: shown in red.</summary>
    public bool IsOverdue => Receivables.IsOverdue(Stored, Today);

    /// <summary>What's still owed, for part-paid invoices.</summary>
    public string? Owing => Stored.XeroAmountPaid > 0 && Receivables.Outstanding(Stored) > 0 ? Money.Rand(Receivables.Outstanding(Stored)) + " still owed" : null;

    public bool IsInXero => Stored.Status == DocumentStatus.InXero;
}
