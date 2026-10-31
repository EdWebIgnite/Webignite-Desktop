using System.Text.RegularExpressions;

namespace Webignite.Core.Documents;

/// <summary>The subject and text of the email that goes with an invoice or quote, with Xero-style [placeholders].</summary>
public sealed record EmailTemplate(string Subject, string Body);

/// <summary>The templates pasted from Xero, kept in Settings.</summary>
public sealed record EmailTemplates
{
    public EmailTemplate Invoice { get; init; } = new(
        "Invoice [Invoice Number] from [Trading Name] for [Contact Name]",
        """
        Hi [Contact First Name],

        Here's invoice [Invoice Number] for [Currency Code] [Invoice Total].

        The amount outstanding of [Currency Code] [Amount Due] is due on [Due Date].

        View and pay your bill online: [Online Invoice Link]

        If you have any questions, please let us know.

        Thanks,
        [Trading Name]
        """);

    public EmailTemplate Quote { get; init; } = new(
        "Quote [Quote Number] from [Trading Name] for [Contact Name]",
        """
        Hi [Contact First Name],

        Thank you for your enquiry.

        Here's quote [Quote Number] for [Currency Code] [Quote Total].

        If you have any questions, please let us know.

        Thanks,
        [Trading Name]
        """);

    public EmailTemplate For(DocumentKind kind) => kind == DocumentKind.Quote ? Quote : Invoice;
}

/// <summary>What fills the placeholders, mostly read back from Xero so the email matches the PDF.</summary>
public sealed record DocumentEmailValues
{
    public required DocumentKind Kind { get; init; }
    public required string ContactName { get; init; }

    /// <summary>The contact person's first name in Xero. When Xero has none, the contact name is used.</summary>
    public string? FirstName { get; init; }
    public required string Number { get; init; }
    public required decimal Total { get; init; }
    public decimal? AmountDue { get; init; }
    public DateOnly? DueOrExpiryDate { get; init; }
    public string? Reference { get; init; }
    public string? OnlineLink { get; init; }
    public required string TradingName { get; init; }
}

/// <summary>A filled-in email, and any placeholder that couldn't be filled.</summary>
public sealed record FilledEmail(string Subject, string Body, IReadOnlyList<string> Unfilled);

public static partial class DocumentEmail
{
    /// <summary>The placeholders that can be used, as Xero names them.</summary>
    public static IReadOnlyList<string> Placeholders(DocumentKind kind) => kind == DocumentKind.Quote
        ? ["[Contact Name]", "[Contact First Name]", "[Quote Number]", "[Quote Total]", "[Expiry Date]", "[Reference]", "[Trading Name]"]
        : ["[Contact Name]", "[Contact First Name]", "[Invoice Number]", "[Invoice Total]", "[Amount Due]", "[Due Date]", "[Reference]",
            "[Online Invoice Link]", "[Trading Name]"];

    public static FilledEmail Fill(EmailTemplate template, DocumentEmailValues values)
    {
        var unfilled = new List<string>();
        var subject = Replace(template.Subject, values, unfilled);
        var body = Replace(template.Body.Replace("\r", ""), values, unfilled);
        return new FilledEmail(subject.Trim(), body.Trim(), unfilled.Distinct().ToList());
    }

    private static string Replace(string text, DocumentEmailValues v, List<string> unfilled)
    {
        // Rand amounts already carry "R", so Xero's [Currency Code] in front of an amount is dropped.
        text = CurrencyCode().Replace(text, "");
        return Placeholder().Replace(text, match =>
        {
            var name = match.Groups[1].Value.Trim().ToLowerInvariant();
            string? value = name switch
            {
                "contact name" => v.ContactName,
                "contact first name" => string.IsNullOrWhiteSpace(v.FirstName) ? v.ContactName : v.FirstName.Trim(),
                "invoice number" or "quote number" or "document number" => v.Number,
                "invoice total" or "quote total" or "total" => Money.Rand(v.Total),
                "amount due" => Money.Rand(v.AmountDue ?? v.Total),
                "due date" or "expiry date" => v.DueOrExpiryDate?.ToString("d MMMM yyyy"),
                "reference" => v.Reference ?? "",
                "online invoice link" or "online quote link" => v.OnlineLink,
                "trading name" or "organisation name" => v.TradingName,
                _ => null,
            };
            if (value is null)
            {
                unfilled.Add(match.Value);
                return match.Value;
            }
            return value;
        });
    }

    [GeneratedRegex(@"\[([A-Za-z ]{2,40})\]")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\[Currency Code\]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyCode();
}
