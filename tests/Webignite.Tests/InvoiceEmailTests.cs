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
    public async Task Approving_puts_the_chosen_branding_theme_on_an_older_draft()
    {
        var xero = new FakeXero();
        var sending = Make(xero, []);
        await sending.PrepareAsync(Stored(DocumentKind.Invoice), "Webignite", "theme-payfast", TestContext.Current.CancellationToken);
        var approve = Assert.Single(xero.Posts).Body;
        Assert.Equal("AUTHORISED", approve["Status"]!.GetValue<string>());
        Assert.Equal("theme-payfast", approve["BrandingThemeID"]!.GetValue<string>());
    }

    [Fact]
    public async Task Approve_and_email_approves_once_and_marks_sent_after()
    {
        var xero = new FakeXero();
        var saves = new List<StoredDocument>();
        var sending = Make(xero, saves);
        var stored = Stored(DocumentKind.Invoice);

        var ready = await sending.PrepareAsync(stored, "Webignite", cancel: TestContext.Current.CancellationToken);

        Assert.Equal(["Invoices/inv1"], xero.Posts.Select(p => p.Path));
        Assert.Equal("AUTHORISED", xero.Posts[0].Body["Status"]!.GetValue<string>());
        Assert.NotNull(ready.Stored.Delivery.Approved);
        Assert.Equal("https://in.xero.com/abc", ready.Values.OnlineLink);
        Assert.Equal("Thandi", ready.Values.FirstName);
        Assert.Equal("accounts@karoocoffee.co.za", ready.ContactEmail);
        Assert.Equal("Invoice INV-0111.pdf", ready.PdfName);
        Assert.Equal((byte)'%', ready.Pdf[0]);

        // Preparing again (say the mail was closed unsent) doesn't approve twice.
        await sending.PrepareAsync(ready.Stored, "Webignite", cancel: TestContext.Current.CancellationToken);
        Assert.Single(xero.Posts);

        var sent = await sending.MarkSentAsync(ready.Stored, [new Webignite.Core.Mail.MailPerson("Thandi Mokoena", "thandi@karoocoffee.co.za")],
            TestContext.Current.CancellationToken);
        Assert.NotNull(sent.Delivery.Emailed);
        Assert.Equal("Thandi Mokoena", sent.Delivery.EmailedTo);
        Assert.True(xero.Posts[^1].Body["SentToContact"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_quote_is_not_approved_and_becomes_sent_after_mailing()
    {
        var xero = new FakeXero();
        var sending = Make(xero, []);
        var ready = await sending.PrepareAsync(Stored(DocumentKind.Quote), "Webignite", cancel: TestContext.Current.CancellationToken);
        Assert.Empty(xero.Posts);
        Assert.Null(ready.Values.OnlineLink);
        await sending.MarkSentAsync(ready.Stored, cancel: TestContext.Current.CancellationToken);
        Assert.Equal("SENT", Assert.Single(xero.Posts).Body["Status"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_sent_quote_is_marked_accepted_or_declined_in_xero_only_once_sent()
    {
        var xero = new FakeXero();
        var sending = Make(xero, []);
        var draft = Stored(DocumentKind.Quote);
        Assert.False(QuoteOutcomes.CanDecide(draft)); // not emailed yet
        await Assert.ThrowsAsync<XeroException>(() => sending.DecideQuoteAsync(draft, accepted: true, TestContext.Current.CancellationToken));

        var ready = await sending.PrepareAsync(draft, "Webignite", cancel: TestContext.Current.CancellationToken);
        var sent = await sending.MarkSentAsync(ready.Stored, cancel: TestContext.Current.CancellationToken);
        Assert.True(QuoteOutcomes.CanDecide(sent));

        var accepted = await sending.DecideQuoteAsync(sent, accepted: true, TestContext.Current.CancellationToken);
        Assert.Equal("ACCEPTED", xero.Posts[^1].Body["Status"]!.GetValue<string>());
        Assert.Equal("k1", xero.Posts[^1].Body["Contact"]!["ContactID"]!.GetValue<string>());
        Assert.Equal(Settlement.Accepted, accepted.Settled?.As);
        Assert.Equal("ACCEPTED", accepted.Xero.Status);
        Assert.False(QuoteOutcomes.CanDecide(accepted));
    }

    [Fact]
    public async Task An_answer_already_given_in_xero_is_kept_and_nothing_written()
    {
        var xero = new FakeXero();
        var sending = Make(xero, []);
        var ready = await sending.PrepareAsync(Stored(DocumentKind.Quote), "Webignite", cancel: TestContext.Current.CancellationToken);
        var sent = await sending.MarkSentAsync(ready.Stored, cancel: TestContext.Current.CancellationToken);
        xero.QuoteStatus = "DECLINED";
        var posts = xero.Posts.Count;

        var kept = await sending.DecideQuoteAsync(sent, accepted: true, TestContext.Current.CancellationToken);
        Assert.Equal(Settlement.Declined, kept.Settled?.As);
        Assert.Equal(posts, xero.Posts.Count);
    }

    [Fact]
    public void An_invoice_made_from_a_quote_remembers_it_and_the_quote_lists_it()
    {
        var quote = Stored(DocumentKind.Quote);
        var invoice = DocumentCopies.Copy(quote, DocumentKind.Invoice, new DocumentSettings(), new DateOnly(2026, 11, 9));
        var copy = DocumentCopies.Copy(quote, DocumentKind.Quote, new DocumentSettings(), new DateOnly(2026, 11, 9));
        Assert.Equal(quote.Id, invoice.FromQuote);
        Assert.Null(copy.FromQuote);
        Assert.Equal("QU-0042", invoice.Document.Reference);
        Assert.Equal([invoice], QuoteOutcomes.InvoicesFrom(quote, [invoice, Stored(DocumentKind.Invoice), copy]));
        Assert.Equal("QU-0042", QuoteOutcomes.Name(quote));
    }

    [Fact]
    public async Task Only_checked_Xero_drafts_can_be_emailed()
    {
        var sending = Make(new FakeXero(), []);
        var stored = Stored(DocumentKind.Invoice).WithXero(x => x with { State = XeroWriteState.Uncertain });
        Assert.NotNull(XeroSending.CannotSend(stored));
        await Assert.ThrowsAsync<XeroException>(() => sending.PrepareAsync(stored, "Webignite", cancel: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Marking_sent_keeps_a_payment_found_while_the_mail_was_written()
    {
        var folder = Directory.CreateTempSubdirectory("webignite-sent-").FullName;
        try
        {
            using var store = new Webignite.Data.LocalStore(Path.Combine(folder, "webignite.db"),
                new Webignite.Data.Vault(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            var old = store.Save(Stored(DocumentKind.Invoice));
            // The payment check runs while Eddie is writing the mail.
            store.Save(old with { Xero = old.Xero with { Status = "PAID", AmountDue = 0m, AmountPaid = 2725m } });

            var sending = Make(new FakeXero(), (s, change) => store.Update(s.Id, change)!);
            var sent = await sending.MarkSentAsync(old, cancel: TestContext.Current.CancellationToken);

            Assert.NotNull(sent.Delivery.Emailed);
            Assert.Equal("PAID", store.Get(old.Id)!.Xero.Status);
            Assert.Equal(2725m, store.Get(old.Id)!.Xero.AmountPaid);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static StoredDocument Stored(DocumentKind kind) => new()
    {
        Id = StoredDocument.NewId(),
        Document = Samples.Completed(Samples.NewInvoice() with { Kind = kind }),
        Status = DocumentStatus.InXero,
        Xero = new()
        {
            Id = kind == DocumentKind.Quote ? "q1" : "inv1",
            Number = kind == DocumentKind.Quote ? "QU-0042" : "INV-0111",
            State = XeroWriteState.Verified,
        },
    };

    private static XeroSending Make(FakeXero xero, List<StoredDocument> saves) =>
        Make(xero, (s, change) => { var changed = change(s); saves.Add(changed); return changed; });

    private static XeroSending Make(FakeXero xero, Func<StoredDocument, Func<StoredDocument, StoredDocument>, StoredDocument> update)
    {
        var tokens = new MemoryTokenStore();
        tokens.WriteRefreshToken("refresh");
        var http = new HttpClient(xero);
        var api = new XeroApi(new XeroSession(new XeroOptions { ClientId = "abc" }, http, tokens), http, "t1");
        return new XeroSending(api, update);
    }

    private sealed class FakeXero : HttpMessageHandler
    {
        public List<(string Path, JsonObject Body)> Posts { get; } = [];
        private string invoiceStatus = "DRAFT";
        public string QuoteStatus { get; set; } = "DRAFT";

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
                        QuoteStatus = status;
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
                "Quotes/q1" => Json($$$"""{"Quotes":[{"QuoteID":"q1","QuoteNumber":"QU-0042","Status":"{{{QuoteStatus}}}","Total":450,"DateString":"2026-10-31T00:00:00","Contact":{"ContactID":"k1","Name":"Karoo Coffee Roasters"}}]}"""),
                "Contacts/k1" => Json("""{"Contacts":[{"ContactID":"k1","Name":"Karoo Coffee Roasters","FirstName":"Thandi","EmailAddress":"accounts@karoocoffee.co.za"}]}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
