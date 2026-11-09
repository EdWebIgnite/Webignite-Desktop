using System.Globalization;
using System.Text.RegularExpressions;

namespace Webignite.Core.Finance;

/// <summary>A bill read from a supplier's PDF, for Eddie to check before it goes to Xero as a draft.</summary>
public sealed record BillDraft
{
    public string Supplier { get; init; } = "";
    public string? Number { get; init; }
    public DateOnly? Date { get; init; }
    public DateOnly? Due { get; init; }
    public decimal? Total { get; init; }
    public string Description { get; init; } = "";
}

/// <summary>
/// Reads what it can from a supplier's bill: who it's from, its number, dates and the amount to pay. It's a first
/// guess shown in boxes to check, never sent as is. Amounts and dates are read the way invoices print them
/// (2 345.67, 1,234.56, 9 Nov 2026, 09/11/2026 as day first), not by this PC's region.
/// </summary>
public static partial class BillReader
{
    public static BillDraft Read(string text, IEnumerable<string> knownSuppliers)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var supplier = Supplier(text, lines, knownSuppliers);
        var number = NumberPattern().Match(text) is { Success: true } n ? n.Groups["n"].Value.Trim() : null;
        var date = DateAfter(lines, DateLabel()) ?? lines.Select(FirstDate).FirstOrDefault(d => d is not null);
        var due = DateAfter(lines, DueLabel());
        var total = Total(lines);
        return new BillDraft
        {
            Supplier = supplier,
            Number = number,
            Date = date,
            Due = due,
            Total = total,
            Description = supplier.Length == 0 ? "Bill" : number is null ? $"Bill from {supplier}" : $"Bill {number} from {supplier}",
        };
    }

    /// <summary>Why the bill can't go to Xero yet, or null when it can.</summary>
    public static string? Check(BillDraft bill) =>
        string.IsNullOrWhiteSpace(bill.Supplier) ? "Type who the bill is from."
        : bill.Total is not > 0 ? "Type the amount to pay."
        : bill.Date is null ? "Pick the bill's date."
        : bill.Due is { } due && due < bill.Date ? "The due date is before the bill's date."
        : null;

    private static string Supplier(string text, List<string> lines, IEnumerable<string> known)
    {
        var match = known.Where(k => k.Trim().Length >= 3 && text.Contains(k.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(k => k.Length).FirstOrDefault();
        if (match is not null)
            return match.Trim();
        return lines.FirstOrDefault(l => l.Any(char.IsLetter) && !NotAName().IsMatch(l) && l.Length <= 60) ?? "";
    }

    private static DateOnly? DateAfter(List<string> lines, Regex label)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (label.Match(lines[i]) is not { Success: true } m)
                continue;
            if (FirstDate(lines[i][(m.Index + m.Length)..]) is { } same)
                return same;
            if (i + 1 < lines.Count && FirstDate(lines[i + 1]) is { } next)
                return next;
        }
        return null;
    }

    public static DateOnly? FirstDate(string line)
    {
        if (IsoDate().Match(line) is { Success: true } iso
            && DateOnly.TryParseExact(iso.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var a))
            return a;
        if (NumericDate().Match(line) is { Success: true } num
            && DateOnly.TryParseExact(num.Value.Replace('.', '/').Replace('-', '/'), ["d/M/yyyy", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var b))
            return b;
        if (WordDate().Match(line) is { Success: true } word)
        {
            var month = Array.FindIndex(CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames,
                m => m.Length > 0 && word.Groups["m"].Value.StartsWith(m, StringComparison.OrdinalIgnoreCase)) + 1;
            if (month > 0 && int.TryParse(word.Groups["d"].Value, CultureInfo.InvariantCulture, out var day)
                && int.TryParse(word.Groups["y"].Value, CultureInfo.InvariantCulture, out var year)
                && day <= DateTime.DaysInMonth(year, month))
                return new DateOnly(year, month, day);
        }
        return null;
    }

    private static decimal? Total(List<string> lines)
    {
        // The amount to pay: on the last line saying so, else the biggest amount on the page.
        foreach (var line in Enumerable.Reverse(lines))
            if (TotalLabel().IsMatch(line) && Amounts(line).LastOrDefault() is > 0 and var labelled)
                return labelled;
        return lines.SelectMany(Amounts).DefaultIfEmpty().Max() is > 0 and var biggest ? biggest : null;
    }

    private static IEnumerable<decimal> Amounts(string line)
    {
        foreach (Match m in AmountPattern().Matches(line))
        {
            var plain = m.Groups["a"].Value.Replace(" ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal)
                .Replace(",", "", StringComparison.Ordinal);
            if (decimal.TryParse(plain, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
                yield return amount;
        }
    }

    [GeneratedRegex(@"(?:invoice|inv|bill|document)\s*(?:no\.?|number|num|#)\s*[:\-]?\s*(?<n>[A-Z0-9][A-Z0-9\-/]{1,24})", RegexOptions.IgnoreCase)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^(?:tax\s+)?invoice\b|^statement\b|^page\b|^bill\s+to\b|^vat\b|^\d", RegexOptions.IgnoreCase)]
    private static partial Regex NotAName();

    [GeneratedRegex(@"(?:invoice|document|tax\s+point|issue)?\s*date\b(?!.*due)", RegexOptions.IgnoreCase)]
    private static partial Regex DateLabel();

    [GeneratedRegex(@"\b(?:due\s*(?:date|by|on)?|payment\s+due|pay\s+by)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DueLabel();

    [GeneratedRegex(@"\b(?:amount\s+due|balance\s+due|total\s+due|amount\s+payable|total\s+payable|grand\s+total|total)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TotalLabel();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\b\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4}\b")]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"\b(?<d>\d{1,2})(?:st|nd|rd|th)?\s+(?<m>[A-Za-z]{3,9})\.?,?\s+(?<y>\d{4})\b")]
    private static partial Regex WordDate();

    [GeneratedRegex(@"(?<![\w.])(?:R|ZAR)?\s?(?<a>\d{1,3}(?:[ , ]\d{3})*\.\d{2}|\d+\.\d{2})(?![\d])")]
    private static partial Regex AmountPattern();
}
