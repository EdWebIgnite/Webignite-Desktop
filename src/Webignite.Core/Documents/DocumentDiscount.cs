using System.Globalization;
using Webignite.Core.Tools;

namespace Webignite.Core.Documents;

/// <summary>
/// The discount on a whole quote or invoice, as typed in one box: "10%" takes a tenth off, "R 1 000" or "1000"
/// takes that many Rand off. It goes on the document as a discount line before the total.
/// </summary>
public static class DocumentDiscount
{
    public sealed record Typed(decimal? Percent, decimal? Amount, string? Problem = null)
    {
        public static readonly Typed None = new(null, null);
    }

    public static Typed Read(string? text)
    {
        var typed = (text ?? "").Trim();
        if (typed.Length == 0)
            return Typed.None;
        if (typed.EndsWith('%'))
        {
            return Calculator.Amount(typed[..^1]) is { } percent && percent > 0 && percent <= 100
                ? new Typed(percent, null)
                : new Typed(null, null, "Type the discount as a percentage up to 100, e.g. 10%.");
        }
        return Calculator.Amount(typed) is { } amount && amount > 0
            ? new Typed(null, Money.Cents(amount))
            : new Typed(null, null, "Type the discount as 10% or an amount in Rand, e.g. 1000.");
    }

    /// <summary>The discount as it's shown in the box: "10%", or the Rand amount without the R.</summary>
    public static string Show(Document doc) =>
        doc.DocumentDiscountPercent is { } percent ? percent.ToString("0.##", CultureInfo.CurrentCulture) + "%"
        : doc.DocumentDiscountAmount is { } amount ? Calculator.Plain(amount)
        : "";

    /// <summary>The words on the discount line: "Discount (10%)" or "Discount".</summary>
    public static string Label(Document doc) =>
        doc.DocumentDiscountPercent is { } percent ? $"Discount ({percent.ToString("0.##", CultureInfo.InvariantCulture)}%)" : "Discount";
}
