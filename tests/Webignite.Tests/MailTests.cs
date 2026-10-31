using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using MimeKit;
using Webignite.Core.Mail;
using Webignite.Data;
using Webignite.Mail;

namespace Webignite.Tests;

public sealed class MailTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;

    [Fact]
    public void Webignite_mailboxes_get_the_xneelo_servers()
    {
        var account = MailAccounts.Suggest("Eddie@Webignite.co.za ", "Eddie Teichert");
        Assert.Equal(("eddie@webignite.co.za", "mail.webignite.co.za", 993, "smtp.webignite.co.za", 465, false),
            (account.Address, account.ImapHost, account.ImapPort, account.SmtpHost, account.SmtpPort, account.SendsInvoices));
        Assert.True(MailAccounts.Suggest("accounts@webignite.co.za", "Webignite Accounts").SendsInvoices);
        Assert.Empty(MailAccounts.Check(account, []));
        Assert.Equal(["That mailbox is already added."], MailAccounts.Check(account, [account with { Id = "other" }]));
    }

    [Theory]
    [InlineData("INBOX", '.', FolderRole.Inbox)]
    [InlineData("INBOX.Sent", '.', FolderRole.Sent)]
    [InlineData("INBOX.Trash", '.', FolderRole.Trash)]
    [InlineData("Deleted Items", '/', FolderRole.Trash)]
    [InlineData("INBOX.spam", '.', FolderRole.Junk)]
    [InlineData("INBOX.Clients.Bosveld", '.', FolderRole.Other)]
    public void Folder_roles_are_guessed_from_names(string path, char separator, FolderRole role) =>
        Assert.Equal(role, MailFolders.Guess(path, separator));

    [Fact]
    public void Html_loses_scripts_forms_and_trackers()
    {
        const string html = """
            <p onclick="steal()">Hi <a href="javascript:alert(1)">there</a></p>
            <script>alert('x')</script><form action="https://evil"><input></form>
            <img src="https://tracker.example/pixel.gif"><img src="data:image/png;base64,AAAA">
            <div style="background:url(https://tracker.example/bg.png)">x</div>
            """;
        var safe = MailHtml.Sanitise(html, allowRemoteImages: false);
        Assert.DoesNotContain("script", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", safe);
        Assert.DoesNotContain("<form", safe);
        Assert.DoesNotContain("tracker.example", safe);
        Assert.Contains("data:image/png", safe);
        Assert.True(MailHtml.HasRemoteImages(html));
        Assert.Contains("tracker.example/pixel.gif", MailHtml.Sanitise(html, allowRemoteImages: true));
    }

    [Fact]
    public void Html_turns_into_readable_text()
    {
        var text = MailHtml.ToText("<html><head><style>p{}</style></head><p>Hi Eddie,</p><p>Invoice&nbsp;attached.<br>Thanks &amp; regards</p></html>");
        Assert.Equal("Hi Eddie,\nInvoice attached.\nThanks & regards", text);
    }

    [Theory]
    [InlineData("Invoice INV-0107", "Re: Invoice INV-0107")]
    [InlineData("RE: Invoice", "RE: Invoice")]
    public void Reply_subjects_do_not_stack(string subject, string expected) => Assert.Equal(expected, MailText.ReplySubject(subject));

    [Fact]
    public void Reader_shows_inline_pictures_and_lists_attachments()
    {
        var builder = new BodyBuilder { HtmlBody = "<p>Logo:</p><img src=\"cid:logo1\">", TextBody = "Logo attached" };
        var logo = builder.LinkedResources.Add("logo.png", [137, 80, 78, 71], new ContentType("image", "png"));
        logo.ContentId = "logo1";
        builder.Attachments.Add("..\\..\\Quote:1.pdf", Encoding.UTF8.GetBytes("%PDF-1.4"), new ContentType("application", "pdf"));
        var message = new MimeMessage { Subject = "Quote", Body = builder.ToMessageBody() };
        message.From.Add(new MailboxAddress("Pieter", "pieter@bosveldbuilders.co.za"));
        using var raw = new MemoryStream();
        message.WriteTo(raw, TestContext.Current.CancellationToken);

        var body = MailReader.Read(raw.ToArray(), allowRemoteImages: false);
        Assert.Contains("data:image/png;base64,", body.Html);
        Assert.Equal("Logo attached", body.Text);
        var attachment = Assert.Single(body.Attachments);
        Assert.Equal("Quote_1.pdf", attachment.FileName);

        using var saved = new MemoryStream();
        MailReader.SaveAttachment(raw.ToArray(), 0, saved);
        Assert.Equal("%PDF-1.4", Encoding.UTF8.GetString(saved.ToArray()));
    }

    [Fact]
    public void Plain_text_mail_keeps_its_lines_and_is_escaped()
    {
        var message = new MimeMessage { Subject = "x", Body = new TextPart("plain") { Text = "Hi <Eddie>\nLine two" } };
        using var raw = new MemoryStream();
        message.WriteTo(raw, TestContext.Current.CancellationToken);
        var body = MailReader.Read(raw.ToArray(), false);
        Assert.Contains("Hi &lt;Eddie&gt;<br>Line two", body.Html);
    }

    [Fact]
    public void Store_caches_mail_encrypted_and_searches_it()
    {
        var path = Path.Combine(folder, "mail.db");
        using (var store = new LocalStore(path, new Vault(RandomNumberGenerator.GetBytes(32))))
        {
            var account = store.SaveMailAccount(MailAccounts.Suggest("eddie@webignite.co.za", "Eddie Teichert"));
            store.SaveMailFolder(new MailFolder { AccountId = account.Id, Path = "INBOX.Sent", Name = "Sent", Role = FolderRole.Sent });
            store.SaveMailFolder(new MailFolder { AccountId = account.Id, Path = "INBOX", Name = "Inbox", Role = FolderRole.Inbox });
            Assert.Equal(["INBOX", "INBOX.Sent"], store.ListMailFolders(account.Id).Select(f => f.Path));

            store.SaveMailSummaries(
            [
                Summary(account.Id, 1, "Bosveld sitemap", "Pieter", DateTimeOffset.Parse("2026-10-30T09:00:00+02:00")),
                Summary(account.Id, 2, "Fynbos photos", "Anri", DateTimeOffset.Parse("2026-10-31T09:00:00+02:00")),
            ]);
            Assert.Equal([2u, 1u], store.ListMailSummaries(account.Id, "INBOX").Select(s => s.Uid));
            Assert.Equal([1u], store.SearchMail([account.Id], "pieter sitemap").Select(s => s.Uid));
            Assert.Equal(new HashSet<uint> { 1, 2 }, store.KnownMailUids(account.Id, "INBOX"));

            store.SaveMailBody(account.Id, "INBOX", 1, Encoding.UTF8.GetBytes("Subject: Bosveld sitemap\r\n\r\nSecret plan"));
            Assert.Equal("Subject: Bosveld sitemap\r\n\r\nSecret plan", Encoding.UTF8.GetString(store.GetMailBody(account.Id, "INBOX", 1)!));

            store.DeleteMailMessages(account.Id, "INBOX", [1]);
            Assert.Null(store.GetMailBody(account.Id, "INBOX", 1));
            Assert.Equal([2u], store.ListMailSummaries(account.Id, "INBOX").Select(s => s.Uid));

            store.KeepOnlyMailFolders(account.Id, ["INBOX"]);
            Assert.Single(store.ListMailFolders(account.Id));
        }
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(path).Concat(File.Exists(path + "-wal") ? File.ReadAllBytes(path + "-wal") : []).ToArray();
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("Fynbos", text);
        Assert.DoesNotContain("Secret plan", text);
    }

    private static MailSummary Summary(string accountId, uint uid, string subject, string from, DateTimeOffset date) => new()
    {
        AccountId = accountId,
        Folder = "INBOX",
        Uid = uid,
        Subject = subject,
        From = new MailPerson(from, from.ToLowerInvariant() + "@example.co.za"),
        Date = date,
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }
}
