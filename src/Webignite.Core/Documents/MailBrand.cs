using System.Globalization;
using System.Net;
using System.Text;
using Webignite.Core.Mail;

namespace Webignite.Core.Documents;

/// <summary>
/// How quote and invoice mails look: the logo, the business's details for the footer, the banking details and the
/// terms link. Typed once in Settings and shared with the other PC; kept off the code, as it holds the bank account.
/// </summary>
public sealed record MailBrand
{
    /// <summary>The logo as Base64 (PNG or JPEG), shown at the top; without one, the company name is.</summary>
    public string? Logo { get; init; }
    public string LogoType { get; init; } = "image/png";

    public string CompanyName { get; init; } = "";
    public string Address { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Email { get; init; } = "";
    public string Website { get; init; } = "";

    /// <summary>The banking details, as they should read, one line each. Shown on invoice mails.</summary>
    public string BankDetails { get; init; } = "";

    /// <summary>Where the full terms and conditions are.</summary>
    public string TermsLink { get; init; } = "";

    /// <summary>The biggest logo kept, so it travels and mails stay light.</summary>
    public const int LargestLogo = 400 * 1024;
}

/// <summary>
/// The branded layout for a quote or invoice mail, in the manner of QuickBooks' mails: the logo, a soft purple panel
/// with the due or valid-until date, the amount and a button (View and pay online for an invoice, Accept this quote
/// for a quote), the person's own words, the banking details (invoices), the terms link and a footer. Built with
/// tables and inline styles, as mail programs need. Text to clients uses <see cref="CultureInfo.InvariantCulture"/>.
/// </summary>
public static class DocumentMailLayout
{
    public const string LogoId = "webignite-logo";

    // From the purple end of the Webignite logo, soft enough for black text; the button is the app's dark grey.
    private const string Panel = "#EEE6F6";
    private const string Accent = "#5B2A86";
    private const string Button = "#36373C";
    private const string Ink = "#2E2F33";
    private const string Muted = "#707278";

    /// <summary>The layout for this quote or invoice. <paramref name="acceptTo"/> is where a quote's Accept button replies to.</summary>
    public static MailLayout Build(DocumentEmailValues values, MailBrand brand, string? acceptTo)
    {
        var quote = values.Kind == DocumentKind.Quote;
        var amount = Money.Rand(quote ? values.Total : values.AmountDue ?? values.Total);
        var date = values.DueOrExpiryDate?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        var heading = date is null ? (quote ? "QUOTE" : "INVOICE") : (quote ? "VALID UNTIL " : "DUE ") + date.ToUpperInvariant();
        var (buttonText, buttonLink) = quote
            ? ("Accept this quote", AcceptLink(values, amount, acceptTo))
            : ("View and pay online", Safe(values.OnlineLink));
        var name = brand.CompanyName.Trim().Length > 0 ? brand.CompanyName.Trim() : values.TradingName;

        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#F4F4F6;padding:24px 0;font-family:Arial,Helvetica,sans-serif;color:{Ink}\"><tr><td align=\"center\">");
        html.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;width:100%;background:#FFFFFF;border-radius:8px\">");
        // Logo, or the name when there's no logo, then the name in the accent colour.
        html.Append("<tr><td align=\"center\" style=\"padding:32px 32px 8px\">");
        if (brand.Logo is { Length: > 0 })
            html.Append(CultureInfo.InvariantCulture, $"<img src=\"cid:{LogoId}\" alt=\"{E(name)}\" style=\"max-width:260px;max-height:90px;display:block;border:0\">");
        html.Append(CultureInfo.InvariantCulture, $"</td></tr><tr><td align=\"center\" style=\"padding:4px 32px 24px;font-size:18px;color:{Accent}\">{E(name)}</td></tr>");
        // The panel: when, how much, and the button.
        html.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"padding:0 24px\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{Panel};border-radius:6px\"><tr><td align=\"center\" style=\"padding:28px 24px\">");
        html.Append(CultureInfo.InvariantCulture, $"<div style=\"font-size:13px;font-weight:bold;letter-spacing:0.5px\">{E(heading)}</div>");
        html.Append(CultureInfo.InvariantCulture, $"<div style=\"font-size:40px;font-weight:bold;margin:6px 0 4px\">{E(amount)}</div>");
        html.Append(CultureInfo.InvariantCulture, $"<div style=\"font-size:13px;color:{Muted};margin-bottom:16px\">{(quote ? "Quote" : "Invoice")} {E(values.Number)}</div>");
        if (buttonLink is not null)
            html.Append(CultureInfo.InvariantCulture, $"<a href=\"{E(buttonLink)}\" style=\"display:inline-block;background:{Button};color:#FFFFFF;text-decoration:none;font-weight:bold;font-size:15px;padding:12px 32px;border-radius:4px\">{E(buttonText)}</a>");
        html.Append("</td></tr></table></td></tr>");
        // The person's words.
        html.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"padding:28px 40px 8px;font-size:15px;line-height:1.5\">{MailLayout.BodyMarker}</td></tr>");
        // Banking details on invoices, then the terms.
        if (!quote && brand.BankDetails.Trim().Length > 0)
            html.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"padding:16px 40px 8px;font-size:15px;line-height:1.5\"><strong>Banking details</strong><br>{Lines(brand.BankDetails)}</td></tr>");
        if (Safe(brand.TermsLink) is { } terms)
            html.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"padding:8px 40px 24px;font-size:15px;line-height:1.5\">Our full terms and conditions are available at:<br><a href=\"{E(terms)}\" style=\"color:{Accent}\">{E(brand.TermsLink.Trim())}</a></td></tr>");
        // The footer.
        html.Append(CultureInfo.InvariantCulture, $"<tr><td style=\"padding:0 40px\"><div style=\"border-top:1px solid #E3E3E8\"></div></td></tr>");
        html.Append(CultureInfo.InvariantCulture, $"<tr><td align=\"center\" style=\"padding:18px 40px 28px;font-size:13px;line-height:1.8;color:{Muted}\">{E(name)}");
        if (brand.Address.Trim().Length > 0)
            html.Append("<br>").Append(E(brand.Address.Trim().ReplaceLineEndings(", ")));
        var contact = new[] { brand.Phone, brand.Email, brand.Website }.Select(c => c.Trim()).Where(c => c.Length > 0).Select(E).ToList();
        if (contact.Count > 0)
            html.Append("<br>").Append(string.Join("&nbsp;&nbsp;&nbsp;", contact));
        html.Append("</td></tr></table></td></tr></table>");

        var text = new StringBuilder();
        if (!quote && brand.BankDetails.Trim().Length > 0)
            text.Append("Banking details\n").Append(brand.BankDetails.Trim().ReplaceLineEndings("\n")).Append("\n\n");
        if (Safe(brand.TermsLink) is { } termsText)
            text.Append("Our full terms and conditions are available at: ").Append(termsText).Append("\n\n");
        if (buttonLink is not null)
            text.Append(buttonText).Append(": ").Append(buttonLink).Append("\n\n");
        text.Append(string.Join("\n", new[] { name, brand.Address.Trim().ReplaceLineEndings(", "), brand.Phone, brand.Email, brand.Website }
            .Select(l => l.Trim()).Where(l => l.Length > 0)));

        IReadOnlyList<InlineImage> images = brand.Logo is { Length: > 0 } logo && TryLogo(logo) is { } bytes
            ? [new InlineImage(LogoId, bytes, brand.LogoType)]
            : [];
        return new MailLayout(html.ToString(), text.ToString(), images);
    }

    /// <summary>A reply to <paramref name="to"/> saying the client accepts the quote, for the quote's button.</summary>
    public static string? AcceptLink(DocumentEmailValues values, string amount, string? to)
    {
        if (string.IsNullOrWhiteSpace(to) || !to.Contains('@', StringComparison.Ordinal))
            return null;
        var subject = Uri.EscapeDataString($"Accepting quote {values.Number}");
        var body = Uri.EscapeDataString($"Hi,\n\nWe accept quote {values.Number} for {amount}. Please go ahead.\n\nThanks");
        return $"mailto:{to.Trim()}?subject={subject}&body={body}";
    }

    // Only web and mail links go in a button or link.
    private static string? Safe(string? link)
    {
        var trimmed = link?.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto" ? trimmed : null;
    }

    private static byte[]? TryLogo(string base64)
    {
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string Lines(string text) =>
        string.Join("<br>", text.Trim().ReplaceLineEndings("\n").Split('\n').Select(l => E(l.Trim())));
}
