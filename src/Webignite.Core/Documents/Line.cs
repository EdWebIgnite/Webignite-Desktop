namespace Webignite.Core.Documents;

/// <summary>One line on a quote or invoice. Leave quantity and unit price empty for a text-only line.</summary>
public sealed record Line
{
    public required string Description { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? DiscountPercent { get; init; }
    public decimal? DiscountAmount { get; init; }

    /// <summary>The line total printed on the source document, used to check an import.</summary>
    public decimal? SourceLineTotal { get; init; }

    /// <summary>A product or service label printed separately from the description.</summary>
    public string? Product { get; init; }

    /// <summary>A Xero item code, when the line is for a saved product.</summary>
    public string? ItemCode { get; init; }

    public bool IsPriced => Quantity is not null;

    public decimal Amount()
    {
        if (Quantity is not { } quantity || UnitPrice is not { } price)
            return 0m;
        var amount = quantity * price;
        if (DiscountPercent is { } percent)
            amount *= (100 - percent) / 100;
        if (DiscountAmount is { } discount)
            amount -= discount;
        return Money.Cents(amount);
    }
}
