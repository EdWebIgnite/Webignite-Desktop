using System.Globalization;

namespace Webignite.Core.Documents;

/// <summary>
/// Numbering quotes and invoices in Webignite, carrying on from the numbers already used in Xero: "INV-0112" is
/// followed by "INV-0113". The next number can be set in Settings, and any document can have its number typed over.
/// </summary>
public static class DocumentNumbers
{
    public const string QuotePrefix = "QU-", InvoicePrefix = "INV-";

    /// <summary>A number split into what comes before its last run of digits, those digits, how many there are, and what follows.</summary>
    public sealed record Parts(string Prefix, long Value, int Width, string Suffix)
    {
        public string Text => Prefix + Value.ToString(CultureInfo.InvariantCulture).PadLeft(Width, '0') + Suffix;
    }

    /// <summary>"INV-0112" as INV-, 112, 4 digits. Null when there are no digits in it.</summary>
    public static Parts? Parse(string? number)
    {
        var text = number?.Trim() ?? "";
        var end = text.Length;
        while (end > 0 && !char.IsAsciiDigit(text[end - 1]))
            end--;
        var start = end;
        while (start > 0 && char.IsAsciiDigit(text[start - 1]))
            start--;
        if (start == end || end - start > 15)
            return null;
        return new Parts(text[..start], long.Parse(text[start..end], CultureInfo.InvariantCulture), end - start, text[end..]);
    }

    /// <summary>Every number in use for this kind: Xero's, and those finished here.</summary>
    public static IEnumerable<string> Used(DocumentKind kind, IEnumerable<StoredDocument> documents) =>
        documents.Where(d => d.Document.Kind == kind && d.Number is { Length: > 0 }).Select(d => d.Number!);

    /// <summary>
    /// The number the next one gets: the one set in Settings, unless that is already used or behind, then one past
    /// the highest used with the same start. With nothing set, it carries on from the highest used, else starts at
    /// QU-0001 or INV-0001.
    /// </summary>
    public static string Next(DocumentKind kind, IEnumerable<StoredDocument> documents, string? typed)
    {
        var used = Used(kind, documents).Select(Parse).OfType<Parts>().ToList();
        var wanted = Parse(typed);
        var shape = wanted ?? used.OrderByDescending(p => p.Value).FirstOrDefault()
            ?? new Parts(kind == DocumentKind.Quote ? QuotePrefix : InvoicePrefix, 0, 4, "");
        var same = used.Where(p => string.Equals(p.Prefix, shape.Prefix, StringComparison.OrdinalIgnoreCase) && string.Equals(p.Suffix, shape.Suffix, StringComparison.OrdinalIgnoreCase)).ToList();
        var after = same.Count == 0 ? 1 : same.Max(p => p.Value) + 1;
        var value = wanted is null ? after : Math.Max(wanted.Value, after);
        return (shape with { Value = value, Width = Math.Max(shape.Width, same.Count == 0 ? shape.Width : same.Max(p => p.Width)) }).Text;
    }

    /// <summary>Why this number can't be used for this document, or null when it can: it must be there and not used by another of the same kind.</summary>
    public static string? Problem(string? number, StoredDocument? self, DocumentKind kind, IEnumerable<StoredDocument> documents)
    {
        if (string.IsNullOrWhiteSpace(number))
            return "Give it a number.";
        if (number.Trim().Length > 255)
            return "That number is too long.";
        var other = documents.FirstOrDefault(d => d.Id != self?.Id && d.Document.Kind == kind
            && string.Equals(d.Number, number.Trim(), StringComparison.OrdinalIgnoreCase));
        return other is null ? null
            : $"{other.Number} is already used{(string.IsNullOrWhiteSpace(other.Document.Customer.Name) ? "" : " for " + other.Document.Customer.Name.Trim())}.";
    }
}
