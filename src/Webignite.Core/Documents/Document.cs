namespace Webignite.Core.Documents;

/// <summary>
/// A quote or invoice, independent of any accounting system. Totals may be left empty on a new
/// document; <see cref="DocumentDefaults.Complete"/> works them out from the lines.
/// </summary>
public sealed record Document
{
    public int SchemaVersion { get; init; } = 1;
    public required DocumentKind Kind { get; init; }
    public required DocumentOrigin Origin { get; init; }

    /// <summary>Empty on a new document: Xero assigns the next number.</summary>
    public string? Number { get; init; }

    public string? Reference { get; init; }
    public required Customer Customer { get; init; }
    public DateOnly? Date { get; init; }
    public DateOnly? DueOrExpiryDate { get; init; }
    public string? Currency { get; init; }
    public TaxTreatment TaxTreatment { get; init; } = TaxTreatment.NoVat;
    public required IReadOnlyList<Line> Lines { get; init; }
    public decimal? Subtotal { get; init; }
    public decimal? DocumentDiscountAmount { get; init; }
    public decimal? DocumentDiscountPercent { get; init; }
    public decimal? TaxTotal { get; init; }
    public decimal? Total { get; init; }
    public string? Notes { get; init; }
    public string? Terms { get; init; }
    public string? OriginalStatus { get; init; }
    public decimal? AmountPaid { get; init; }
    public decimal? BalanceDue { get; init; }
    public IReadOnlyList<string> ReviewFlags { get; init; } = [];

    public decimal LinesTotal() => Lines.Sum(line => line.Amount());

    public decimal Discount()
    {
        if (DocumentDiscountAmount is { } amount)
            return Money.Cents(amount);
        if (DocumentDiscountPercent is { } percent)
            return Money.Cents((Subtotal ?? LinesTotal()) * percent / 100);
        return 0m;
    }

    public decimal NetSubtotal() => Money.Cents((Subtotal ?? LinesTotal()) - Discount());
}
