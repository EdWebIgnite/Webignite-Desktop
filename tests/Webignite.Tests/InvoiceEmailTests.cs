using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Webignite.Core.Documents;
using Webignite.Core.Mail;
using Webignite.Core.Xero;
using Webignite.Mail;
using Webignite.Xero;
using Webignite.Xero.Auth;

namespace Webignite.Tests;

public sealed class InvoiceEmailTests
{
    private static readonly DocumentEmailValues Karoo = new()
    {
        Kind = DocumentKind.Invoice,
        ContactName = "Karoo Coffee Roasters",
        FirstName = "Thandi",
        Number = "INV-0111",
        Total = 2725m,
        AmountDue = 2725m,
        DueOrExpiryDate = new DateOnly(2026, 11, 7),
        OnlineLink = "https://in.xero.com/abc",
        TradingName = "Webignite",
    };

    [Fact]
    public void Xero_template_is_filled_with_rand_amounts()
    {
        var filled = DocumentEmail.Fill(new EmailTemplates().Invoice, Karoo);
        Assert.Equal("Invoice INV-0111 from Webignite for Karoo Coffee Roasters", filled.Subject);
        Assert.StartsWith("Hi Thandi,", filled.Body);
        Assert.Contains("for " + Money.Rand(2725m) + ".", filled.Body);
        Assert.Contains("due on 7 November 2026", filled.Body);
        Assert.Contains("https://in.xero.com/abc", filled.Body);
        Assert.DoesNotContain("[", filled.Body);
        Assert.DoesNotContain("ZAR", filled.Body);
        Assert.Empty(filled.Unfilled);
    }

    [Fact]
    public void Unknown_or_missing_placeholders_are_reported()
    {
        var filled = DocumentEmail.Fill(new EmailTemplate("[Invoice Number]", "Hi [contact first name], [Online Invoice Link] [Payment Terms]"),
            Karoo with { FirstName = null, OnlineLink = null });
        Assert.Equal("Hi Karoo Coffee Roasters, [Online Invoice Link] [Payment Terms]", filled.Body);
        Assert.Equal(["[Online Invoice Link]", "[Payment Terms]"], filled.Unfilled);
    }

    [Fact]
    public void Signature_pictures_are_checked_and_go_into_the_html()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];
        Assert.Equal("image/png", SignatureImages.Check(png).Type);
        Assert.NotNull(SignatureImages.Check([1, 2, 3]).Problem);
        Assert.NotNull(SignatureImages.Check(new byte[SignatureImages.MaxBytes + 1]).Problem);

        var account = new MailAccount
        {
            Id = "a", DisplayName = "Webignite Accounts", Address = "accounts@webignite.co.za", ImapHost = "i", SmtpHost = "s",
            Signature = "Webignite Accounts", SignatureImage = png, SignatureImageType = "image/png",
        };
        var signed = MailComposer.Build(new OutgoingMail { AccountId = "a", Body = "Hi\n\nWebignite Accounts" }, account);
        Assert.Contains("cid:", signed.HtmlBody);
        Assert.Single(signed.BodyParts.OfType<MimeKit.MimePart>(), p => p.ContentType.MimeType == "image/png");

        var unsigned = MailComposer.Build(new OutgoingMail { AccountId = "a", Body = "Hi" }, account);
        Assert.DoesNotContain("cid:", unsigned.HtmlBody);
    }

    [Fact]
    public async Task Approve_and_email_approves_once_and_marks_sent_after()
    {
        var xero = new FakeXero();
        var saves = new List<StoredDocument>();
        var sending = Make(xero, saves);
        var stored = Stored(DocumentKind.Invoice);

        var ready = await sending.PrepareAsync(stored, "Webignite", TestContext.Current.CancellationToken);

        Assert.Equal(["Invoices/inv1"], xero.Posts.Select(p => p.Path));
        Assert.Equal("AUTHORISED", xero.Posts[0].Body["Status"]!.GetValue<string>());
        Assert.NotNull(ready.Stored.Approved);
        Assert.Equal("https://in.xero.com/abc", ready.Values.OnlineLink);
        Assert.Equal("Thandi", ready.Values.FirstName);
        Assert.Equal("accounts@karoocoffee.co.za", ready.ContactEmail);
        Assert.Equal("Invoice INV-0111.pdf", ready.PdfName);
        Assert.Equal((byte)'%', ready.Pdf[0]);

        // Preparing again (say the mail was closed unsent) doesn't approve twice.
        await sending.PrepareAsync(ready.Stored, "Webignite", TestContext.Current.CancellationToken);
        Assert.Single(xero.Posts);

        var sent = await sending.MarkSentAsync(ready.Stored, TestContext.Current.CancellationToken);
        Assert.NotNull(sent.Emailed);
        Assert.True(xero.Posts[^1].Body["SentToContact"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_quote_is_not_approved_and_becomes_sent_after_mailing()
    {
        var xero = new FakeXero();
        var sending = Make(xero, []);
        var ready = await sending.PrepareAsync(Stored(DocumentKind.Quote), "Webignite", TestContext.Current.CancellationToken);
        Assert.Empty(xero.Posts);
        Assert.Null(ready.Values.OnlineLink);
        await sending.MarkSentAsync(ready.Stored, TestContext.Current.CancellationToken);
        Assert.Equal("SENT", Assert.Single(xero.Posts).Body["Status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Only_checked_Xero_drafts_can_be_emailed()
    {
        var sending = Make(new FakeXero(), []);
        var stored = Stored(DocumentKind.Invoice) with { XeroState = XeroWriteState.Uncertain };
        Assert.NotNull(XeroSending.CannotSend(stored));
        await Assert.ThrowsAsync<XeroException>(() => sending.PrepareAsync(stored, "Webignite", TestContext.Current.CancellationToken));
    }

    private static StoredDocument Stored(DocumentKind kind) => new()
    {
        Id = StoredDocument.NewId(),
        Document = Samples.Completed(Samples.NewInvoice() with { Kind = kind }),
        XeroId = kind == DocumentKind.Quote ? "q1" : "inv1",
        XeroNumber = kind == DocumentKind.Quote ? "QU-0042" : "INV-0111",
        XeroState = XeroWriteState.Verified,
        Status = DocumentStatus.InXero,
    };

    private static XeroSending Make(FakeXero xero, List<StoredDocument> saves)
    {
        var tokens = new MemoryTokenStore();
        tokens.WriteRefreshToken("refresh");
        var http = new HttpClient(xero);
        var api = new XeroApi(new XeroSession(new XeroOptions { ClientId = "abc" }, http, tokens), http, "t1");
        return new XeroSending(api, s => { saves.Add(s); return s; });
    }

    private sealed class FakeXero : HttpMessageHandler
    {
        public List<(string Path, JsonObject Body)> Posts { get; } = [];
        private string invoiceStatus = "DRAFT";
        private string quoteStatus = "DRAFT";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "identity.xero.com")
                return Json("""{"access_token":"a","refresh_token":"r","expires_in":1800}""");
            var path = uri.PathAndQuery["/api.xro/2.0/".Length..];
            if (request.Method == HttpMethod.Post)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancel))!.AsObject();
                Posts.Add((path, body));
                if (body["Status"]?.GetValue<string>() is { } status)
                {
                    if (path.StartsWith("Quotes", StringComparison.Ordinal))
                        quoteStatus = status;
                    else
                        invoiceStatus = status;
                }
                return Json("{}");
            }
            if (request.Headers.Accept.Any(a => a.MediaType == "application/pdf"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4 test")) };
            return path switch
            {
                "Invoices/inv1/OnlineInvoice" => Json("""{"OnlineInvoices":[{"OnlineInvoiceUrl":"https://in.xero.com/abc"}]}"""),
                "Invoices/inv1" => Json($$$"""{"Invoices":[{"InvoiceID":"inv1","InvoiceNumber":"INV-0111","Status":"{{{invoiceStatus}}}","Total":450,"AmountDue":450,"DueDateString":"2026-11-07T00:00:00","Contact":{"ContactID":"k1","Name":"Karoo Coffee Roasters"}}]}"""),
                "Quotes/q1" => Json($$$"""{"Quotes":[{"QuoteID":"q1","QuoteNumber":"QU-0042","Status":"{{{quoteStatus}}}","Total":450,"DateString":"2026-10-31T00:00:00","Contact":{"ContactID":"k1","Name":"Karoo Coffee Roasters"}}]}"""),
                "Contacts/k1" => Json("""{"Contacts":[{"ContactID":"k1","Name":"Karoo Coffee Roasters","FirstName":"Thandi","EmailAddress":"accounts@karoocoffee.co.za"}]}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
