namespace Webignite.Core.Documents;

/// <summary>Defaults applied to new documents.</summary>
public sealed record DocumentSettings
{
    public string Currency { get; init; } = "ZAR";
    public int InvoiceDueDays { get; init; } = 7;
    public int QuoteExpiryDays { get; init; } = 30;

    /// <summary>The fields of Eddie's own that quotes and invoices offer (<see cref="DocumentFields"/>), Project to begin with.</summary>
    public IReadOnlyList<FieldDefinition> Fields { get; init; } = DocumentFields.Usual;

    /// <summary>New quotes and invoices are finished, sent and paid in Webignite, never put in Xero. Ones already in Xero carry on there.</summary>
    public bool OnItsOwn { get; init; }

    /// <summary>The next quote number as typed in Settings, e.g. "QU-1025"; null to carry on from the highest used (<see cref="DocumentNumbers"/>).</summary>
    public string? NextQuoteNumber { get; init; }

    /// <summary>The next invoice number as typed in Settings, e.g. "INV-0113"; null to carry on from the highest used.</summary>
    public string? NextInvoiceNumber { get; init; }
}
