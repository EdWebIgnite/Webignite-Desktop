using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

/// <summary>One line in the quotes or invoices list.</summary>
public sealed record DocumentRow(StoredDocument Stored)
{
    public string Number => Stored.XeroNumber ?? "Not in Xero yet";
    public string Customer => string.IsNullOrWhiteSpace(Stored.Document.Customer.Name) ? "No customer yet" : Stored.Document.Customer.Name;
    public string Date => Stored.Document.Date?.ToString("d MMM yyyy") ?? "";
    public string Total => Money.Rand(Stored.Document.Total ?? 0);

    public string Status => Stored.Status switch
    {
        DocumentStatus.Draft => "Draft",
        DocumentStatus.AwaitingApproval => "To approve",
        DocumentStatus.InXero => "In Xero",
        _ => Stored.Status.ToString(),
    };

    public bool IsInXero => Stored.Status == DocumentStatus.InXero;
}
