namespace Webignite.Core.Documents;

/// <summary>Defaults applied to new documents.</summary>
public sealed record DocumentSettings
{
    public string Currency { get; init; } = "ZAR";
    public int InvoiceDueDays { get; init; } = 7;
    public int QuoteExpiryDays { get; init; } = 30;
}
