using System.Globalization;

namespace Webignite.Core.Documents;

/// <summary>Which quotes or invoices the list shows, by where they stand.</summary>
public enum DocumentShow
{
    All,

    /// <summary>Still on this PC: being written, or waiting for approval.</summary>
    NotInXero,

    InXero,
}

/// <summary>Which dates the list shows.</summary>
public enum DocumentPeriod
{
    AllTime,
    ThisMonth,
    LastMonth,

    /// <summary>The South African tax year: 1 March to the end of February.</summary>
    ThisTaxYear,
}

/// <summary>
/// The search box and filters above the quote and invoice lists. Words are matched against the customer,
/// number, reference and line descriptions; a number like 2800 or 2 800 also finds that total.
/// </summary>
public sealed record DocumentFilter(string Text = "", DocumentShow Show = DocumentShow.All, DocumentPeriod Period = DocumentPeriod.AllTime)
{
    public bool IsActive => Text.Trim().Length > 0 || Show != DocumentShow.All || Period != DocumentPeriod.AllTime;

    public IReadOnlyList<StoredDocument> Apply(IEnumerable<StoredDocument> documents, DateOnly today) =>
        documents.Where(d => Matches(d, today)).ToList();

    public bool Matches(StoredDocument stored, DateOnly today)
    {
        var shows = Show switch
        {
            DocumentShow.NotInXero => stored.Status != DocumentStatus.InXero,
            DocumentShow.InXero => stored.Status == DocumentStatus.InXero,
            _ => true,
        };
        return shows && InPeriod(stored.Document.Date, today) && Words().All(word => Found(stored, word));
    }

    private IEnumerable<string> Words() => Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is var words
        && AsAmount(Text) is not null ? [Text.Trim()] : words;

    private bool InPeriod(DateOnly? date, DateOnly today)
    {
        if (Period == DocumentPeriod.AllTime)
            return true;
        if (date is not { } d)
            return false;
        var (from, to) = Range(Period, today);
        return d >= from && d <= to;
    }

    /// <summary>The first and last day of a period, counted from today.</summary>
    public static (DateOnly From, DateOnly To) Range(DocumentPeriod period, DateOnly today)
    {
        var month = new DateOnly(today.Year, today.Month, 1);
        return period switch
        {
            DocumentPeriod.ThisMonth => (month, month.AddMonths(1).AddDays(-1)),
            DocumentPeriod.LastMonth => (month.AddMonths(-1), month.AddDays(-1)),
            DocumentPeriod.ThisTaxYear => TaxYear(today),
            _ => (DateOnly.MinValue, DateOnly.MaxValue),
        };
    }

    /// <summary>1 March to the last day of February, for the tax year today falls in.</summary>
    public static (DateOnly From, DateOnly To) TaxYear(DateOnly today)
    {
        var start = new DateOnly(today.Month >= 3 ? today.Year : today.Year - 1, 3, 1);
        return (start, start.AddYears(1).AddDays(-1));
    }

    private static bool Found(StoredDocument stored, string word)
    {
        if (AsAmount(word) is { } amount)
            return stored.Document.Total == amount || stored.Document.Lines.Any(l => l.Amount() == amount);
        var doc = stored.Document;
        return Has(doc.Customer.Name, word) || Has(stored.XeroNumber, word) || Has(doc.Reference, word)
            || doc.Lines.Any(l => Has(l.Description, word));
    }

    private static bool Has(string? text, string word) => text?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>"2800", "2 800", "R2 800.00" or "2800,50" read as an amount; anything with letters is a word.</summary>
    private static decimal? AsAmount(string text)
    {
        var plain = text.Trim().Replace(" ", "").Replace(" ", "");
        if (plain.StartsWith('R') || plain.StartsWith('r'))
            plain = plain[1..];
        if (plain.Length == 0 || !plain.All(c => char.IsDigit(c) || c is '.' or ','))
            return null;
        plain = plain.Replace(',', '.');
        return decimal.TryParse(plain, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
