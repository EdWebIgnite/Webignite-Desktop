namespace Webignite.Core.Documents;

/// <summary>Defaults applied to new documents.</summary>
public sealed record DocumentSettings
{
    public string Currency { get; init; } = "ZAR";
    public int InvoiceDueDays { get; init; } = 7;
    public int QuoteExpiryDays { get; init; } = 30;

    /// <summary>
    /// Quotes and invoices are made final in Webignite itself, numbered here and sent with Webignite's own PDF, instead
    /// of going to Xero (<see cref="OwnInvoicing"/>). Off until Eddie switches over; Xero runs alongside until then.
    /// </summary>
    public bool MakeInWebignite { get; init; }

    /// <summary>The fields of Eddie's own that quotes and invoices offer (<see cref="DocumentFields"/>), Project to begin with.</summary>
    public IReadOnlyList<FieldDefinition> Fields { get; init; } = DocumentFields.Usual;
}
