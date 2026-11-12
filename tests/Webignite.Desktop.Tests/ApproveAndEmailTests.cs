using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Webignite.Core.Mail;
using Webignite.Core.Xero;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>
/// Approve and email, start to finish, as the invoice editor does it: nothing is approved until the person says
/// yes, the mail opens with Xero's PDF and is not sent, and the invoice is marked emailed only once the mail has gone.
/// </summary>
public sealed class ApproveAndEmailTests
{
    private sealed record Prepared(OutgoingMail Mail, Func<IReadOnlyList<MailPerson>, Task>? AfterSent);

    private static (TestApp App, FakeXero Xero, StoredDocument Invoice, List<Prepared> Opened) Start(bool withMailbox = true)
    {
        var xero = new FakeXero();
        var app = new TestApp(xero);
        if (withMailbox)
        {
            app.Store.SaveMailAccount(new MailAccount
            {
                Id = "accounts", DisplayName = "Karoo Studio Accounts", Address = "accounts@karoostudio.co.za",
                ImapHost = "mail.example", SmtpHost = "mail.example", SendsInvoices = true,
            });
        }
        var draft = app.Add(DocumentKind.Invoice, "Karoo Coffee Roasters", "Website hosting", 450m, DocumentStatus.InXero);
        var invoice = app.Store.Save(draft with { Xero = draft.Xero with { Id = "inv1", Number = "INV-0111", State = XeroWriteState.Verified } });
        var opened = new List<Prepared>();
        // Where the main window would open the mail in Mail: here it is only noted.
        app.Services.Go.ComposeMailAction = (mail, _, afterSent, _) =>
        {
            opened.Add(new Prepared(mail, afterSent));
            return true;
        };
        return (app, xero, invoice, opened);
    }

    [Fact]
    public Task Nothing_is_approved_until_the_person_says_yes() => Ui.Run(async () =>
    {
        var (app, xero, invoice, opened) = Start();
        using var _ = app;
        var editor = new DocumentEditorViewModel(invoice, app.Services, () => { });

        await editor.StartEmailCommand.ExecuteAsync(null);

        Assert.True(editor.ConfirmingApprove);
        Assert.Empty(xero.Posts);
        Assert.Empty(opened);
        Assert.Null(app.Store.Get(invoice.Id)!.Delivery.Approved);

        editor.CancelApproveCommand.Execute(null);
        Assert.False(editor.ConfirmingApprove);
        Assert.Empty(xero.Posts);
    });

    [Fact]
    public Task Saying_yes_approves_it_in_Xero_and_opens_the_mail_with_the_PDF_unsent() => Ui.Run(async () =>
    {
        var (app, xero, invoice, opened) = Start();
        using var _ = app;
        var editor = new DocumentEditorViewModel(invoice, app.Services, () => { });
        await editor.StartEmailCommand.ExecuteAsync(null);

        await editor.ConfirmApproveCommand.ExecuteAsync(null);

        // Approved in Xero, once.
        var approve = Assert.Single(xero.Posts, p => p.Body["Status"]?.GetValue<string>() == "AUTHORISED");
        Assert.StartsWith("Invoices", approve.Path, StringComparison.Ordinal);
        var stored = app.Store.Get(invoice.Id)!;
        Assert.NotNull(stored.Delivery.Approved);
        // The mail is open from the invoice mailbox, to the address Xero has, with Xero's PDF. It has not gone.
        var mail = Assert.Single(opened).Mail;
        Assert.Equal("accounts", mail.AccountId);
        Assert.Equal("accounts@karoocoffee.co.za", Assert.Single(mail.To).Address);
        Assert.Contains("INV-0111", mail.Subject, StringComparison.Ordinal);
        var pdf = Assert.Single(mail.Attachments);
        Assert.EndsWith(".pdf", pdf.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(pdf.FilePath));
        Assert.Null(stored.Delivery.Emailed);
        Assert.False(editor.XeroBusy);
    });

    [Fact]
    public Task The_invoice_is_marked_emailed_only_once_the_mail_has_gone() => Ui.Run(async () =>
    {
        var (app, _, invoice, opened) = Start();
        using var __ = app;
        var editor = new DocumentEditorViewModel(invoice, app.Services, () => { });
        await editor.StartEmailCommand.ExecuteAsync(null);
        await editor.ConfirmApproveCommand.ExecuteAsync(null);
        var prepared = Assert.Single(opened);
        Assert.Null(app.Store.Get(invoice.Id)!.Delivery.Emailed);

        // Send was pressed and the Undo wait is over.
        await prepared.AfterSent!([new MailPerson("Thandi", "accounts@karoocoffee.co.za")]);

        var sent = app.Store.Get(invoice.Id)!;
        Assert.NotNull(sent.Delivery.Emailed);
        Assert.Contains("Thandi", sent.Delivery.EmailedTo, StringComparison.Ordinal);
    });

    [Fact]
    public Task With_no_invoice_mailbox_nothing_is_approved_and_it_says_what_to_set_up() => Ui.Run(async () =>
    {
        var (app, xero, invoice, opened) = Start(withMailbox: false);
        using var _ = app;
        var editor = new DocumentEditorViewModel(invoice, app.Services, () => { });
        await editor.StartEmailCommand.ExecuteAsync(null);

        await editor.ConfirmApproveCommand.ExecuteAsync(null);

        Assert.Empty(xero.Posts);
        Assert.Empty(opened);
        Assert.Equal(MailDrafts.NoInvoiceMailbox, editor.XeroMessage);
        Assert.Null(app.Store.Get(invoice.Id)!.Delivery.Approved);
    });

    /// <summary>Xero, as far as approving and mailing an invoice needs: one draft invoice that becomes approved when told to.</summary>
    private sealed class FakeXero : HttpMessageHandler
    {
        public List<(string Path, JsonObject Body)> Posts { get; } = [];
        private string status = "DRAFT";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "identity.xero.com")
                return Json("""{"access_token":"a","refresh_token":"r","expires_in":1800}""");
            var path = uri.PathAndQuery["/api.xro/2.0/".Length..];
            if (request.Method == HttpMethod.Post)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                Posts.Add((path, body));
                if (body["Status"]?.GetValue<string>() is { } told)
                    status = told;
                return Json("{}");
            }
            if (request.Headers.Accept.Any(a => a.MediaType == "application/pdf"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4 test")) };
            return path switch
            {
                "Invoices/inv1/OnlineInvoice" => Json("""{"OnlineInvoices":[{"OnlineInvoiceUrl":"https://in.xero.com/abc"}]}"""),
                "Invoices/inv1" => Json($$$"""{"Invoices":[{"InvoiceID":"inv1","InvoiceNumber":"INV-0111","Status":"{{{status}}}","Total":450,"AmountDue":450,"DueDateString":"2026-11-19T00:00:00","Contact":{"ContactID":"k1","Name":"Karoo Coffee Roasters"}}]}"""),
                "Contacts/k1" => Json("""{"Contacts":[{"ContactID":"k1","Name":"Karoo Coffee Roasters","EmailAddress":"accounts@karoocoffee.co.za","FirstName":"Thandi"}]}"""),
                _ => Json("{}"),
            };
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
