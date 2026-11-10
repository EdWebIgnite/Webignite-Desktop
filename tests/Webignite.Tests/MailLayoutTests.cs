using Webignite.Core.Documents;
using Webignite.Core.Mail;

namespace Webignite.Tests;

/// <summary>The branded layout around quote and invoice mails.</summary>
public sealed class MailLayoutTests
{
    private static readonly MailBrand Brand = new()
    {
        Logo = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]),
        CompanyName = "Webignite (Pty) Ltd",
        Address = "10 Apricot\n20 Glen Ave, Pretoria",
        Phone = "082 000 0000",
        Email = "accounts@example.co.za",
        Website = "www.example.co.za",
        BankDetails = "Some Bank\nCheque account\nAccount number: 000000",
        TermsLink = "https://example.co.za/terms/",
    };

    private static DocumentEmailValues Invoice() => new()
    {
        Kind = DocumentKind.Invoice, ContactName = "Karoo Coffee", Number = "INV-0107", Total = 2075m, AmountDue = 2075m,
        DueOrExpiryDate = new DateOnly(2026, 11, 17), OnlineLink = "https://in.xero.com/abc", TradingName = "Webignite",
    };

    [Fact]
    public void An_invoice_mail_has_the_logo_the_amount_due_a_pay_button_the_bank_details_terms_and_footer()
    {
        var layout = DocumentMailLayout.Build(Invoice(), Brand, "accounts@example.co.za");
        Assert.Contains("cid:" + DocumentMailLayout.LogoId, layout.Html, StringComparison.Ordinal);
        Assert.Contains("DUE 17 NOVEMBER 2026", layout.Html, StringComparison.Ordinal);
        Assert.Contains(Money.Rand(2075m), System.Net.WebUtility.HtmlDecode(layout.Html), StringComparison.Ordinal);
        Assert.Contains("href=\"https://in.xero.com/abc\"", layout.Html, StringComparison.Ordinal);
        Assert.Contains("View and pay online", layout.Html, StringComparison.Ordinal);
        Assert.Contains("Account number: 000000", layout.Html, StringComparison.Ordinal);
        Assert.Contains("https://example.co.za/terms/", layout.Html, StringComparison.Ordinal);
        Assert.Contains("10 Apricot, 20 Glen Ave, Pretoria", layout.Html, StringComparison.Ordinal);
        Assert.Contains(MailLayout.BodyMarker, layout.Html, StringComparison.Ordinal);
        Assert.Equal(DocumentMailLayout.LogoId, Assert.Single(layout.Images).ContentId);
        Assert.Contains("Account number: 000000", layout.TextAfter, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quote_mail_says_valid_until_and_its_button_replies_to_accept_without_bank_details()
    {
        var quote = Invoice() with { Kind = DocumentKind.Quote, Number = "QU-1020", Total = 1000m, AmountDue = null, OnlineLink = null };
        var layout = DocumentMailLayout.Build(quote, Brand, "accounts@example.co.za");
        Assert.Contains("VALID UNTIL 17 NOVEMBER 2026", layout.Html, StringComparison.Ordinal);
        Assert.Contains("Accept this quote", layout.Html, StringComparison.Ordinal);
        Assert.Contains("mailto:accounts@example.co.za?subject=Accepting%20quote%20QU-1020", layout.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Account number", layout.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_logo_or_link_the_name_shows_and_there_is_no_button_and_odd_links_are_left_out()
    {
        var layout = DocumentMailLayout.Build(Invoice() with { OnlineLink = null },
            Brand with { Logo = null, TermsLink = "javascript:alert(1)", CompanyName = "<b>Web</b>" }, null);
        Assert.Empty(layout.Images);
        Assert.DoesNotContain("View and pay online", layout.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript", layout.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;Web&lt;/b&gt;", layout.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_typed_words_go_inside_the_layout_and_its_text_under_the_plain_version()
    {
        var layout = new MailLayout($"<html>{MailLayout.BodyMarker}</html>", "Banking details", []);
        var mail = new OutgoingMail { AccountId = "a", Body = "Hi Thandi" , Layout = layout };
        Assert.StartsWith("<html><div", Replies.HtmlPart(mail), StringComparison.Ordinal);
        Assert.Contains("Hi Thandi", Replies.HtmlPart(mail), StringComparison.Ordinal);
        Assert.EndsWith("Banking details", Replies.TextPart(mail).TrimEnd(), StringComparison.Ordinal);
    }
}
