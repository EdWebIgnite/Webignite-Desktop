using System.Security.Cryptography;
using MimeKit;
using Webignite.Core.Mail;
using Webignite.Data;
using Webignite.Mail;

namespace Webignite.Tests;

public sealed class AssistantMailTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private readonly LocalStore store;
    private readonly AssistantTools tools;

    private static readonly MailAccount Work = new()
    {
        Id = "work", DisplayName = "Eddie", Address = "eddie@webignite.co.za",
        ImapHost = "mail.webignite.co.za", SmtpHost = "smtp.webignite.co.za", Signature = "Eddie\nWebignite",
    };

    private static readonly MailAccount Accounts = Work with
    {
        Id = "accounts", DisplayName = "Webignite Accounts", Address = "accounts@webignite.co.za", Signature = null,
    };

    public AssistantMailTests()
    {
        store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        // No password is saved, so nothing here can reach a mail server.
        tools = new AssistantTools(store, new MailService(store, new MemorySecretStore()));
    }

    [Theory]
    [InlineData("work/44/INBOX", "work", 44u, "INBOX")]
    [InlineData("work/7/INBOX/Clients/Karoo", "work", 7u, "INBOX/Clients/Karoo")]
    public void Mail_ids_round_trip(string id, string account, uint uid, string path)
    {
        Assert.Equal((account, uid, path), AssistantMail.ParseId(id));
        Assert.Equal(id, AssistantMail.Id(new MailSummary { AccountId = account, Folder = path, Uid = uid }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("work/x/INBOX")]
    [InlineData("work/4")]
    public void Bad_ids_are_refused(string id) => Assert.Null(AssistantMail.ParseId(id));

    [Fact]
    public void Mailboxes_are_found_by_address_short_name_or_name()
    {
        MailAccount[] both = [Work, Accounts];
        Assert.Equal(Accounts, AssistantMail.FindMailbox(both, "accounts@webignite.co.za"));
        Assert.Equal(Accounts, AssistantMail.FindMailbox(both, "accounts"));
        Assert.Equal(Accounts, AssistantMail.FindMailbox(both, "Webignite Accounts"));
        Assert.Null(AssistantMail.FindMailbox(both, "info"));
    }

    [Fact]
    public async Task Assistants_only_see_shared_mailboxes()
    {
        Seed();
        store.SaveMailAccount(Accounts with { AssistantsMayRead = false });

        Assert.Equal(["eddie@webignite.co.za"], tools.ListMailboxes().Select(m => m.Address));
        var all = await tools.SearchAsync(null, null, null, false, 20, TestContext.Current.CancellationToken);
        Assert.All(all.Mail, m => Assert.Equal("eddie@webignite.co.za", m.Mailbox));
        await Assert.ThrowsAsync<AssistantException>(() => tools.ReadAsync("accounts/1/INBOX", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Search_uses_the_copy_on_this_PC_when_the_server_cannot_be_reached()
    {
        Seed();
        var result = await tools.SearchAsync("karoo", null, null, unreadOnly: false, limit: 10, TestContext.Current.CancellationToken);
        var found = Assert.Single(result.Mail);
        Assert.Equal("Proof of payment INV-0107", found.Subject);
        Assert.Equal("accounts@webignite.co.za", found.Mailbox);
        Assert.Contains("copy on Eddie's PC", result.Note);

        var unread = await tools.SearchAsync(null, "eddie", null, unreadOnly: true, limit: 10, TestContext.Current.CancellationToken);
        Assert.Equal(["Booking page"], unread.Mail.Select(m => m.Subject));
    }

    [Fact]
    public async Task Search_by_client_finds_mail_with_their_domain()
    {
        Seed();
        store.SaveClient(new Webignite.Core.Clients.Client { Id = "c", Name = "Fynbos Physio", Website = "fynbosphysio.co.za" });
        var result = await tools.SearchAsync(null, null, null, false, 20, TestContext.Current.CancellationToken, client: "fynbos");
        Assert.Equal(["Booking page"], result.Mail.Select(m => m.Subject));
        var none = await tools.SearchAsync(null, null, null, false, 20, TestContext.Current.CancellationToken, client: "Nobody");
        Assert.Empty(none.Mail);
        Assert.Contains("No client called", none.Note);
    }

    [Fact]
    public async Task Reading_gives_plain_text_and_the_warning()
    {
        Seed();
        var mail = await tools.ReadAsync("work/41/INBOX", TestContext.Current.CancellationToken);
        Assert.Equal(AssistantMail.Warning, mail.Warning);
        Assert.Contains("It just spins.", mail.Text);
        Assert.DoesNotContain("<p>", mail.Text);
        Assert.Equal("Lerato <lerato@fynbosphysio.co.za>", mail.From);
    }

    [Fact]
    public async Task A_reply_draft_is_addressed_threaded_and_signed()
    {
        Seed();
        var (account, draft) = await tools.BuildDraftAsync(null, "work/41/INBOX", false, null, null, null, "Hi Lerato,\n\nLooking at it now.",
            TestContext.Current.CancellationToken);
        Assert.Equal("work", account.Id);
        Assert.Equal(["lerato@fynbosphysio.co.za"], draft.To.Select(p => p.Address));
        Assert.Equal("Re: Booking page", draft.Subject);
        Assert.Equal("booking@fynbosphysio.co.za", draft.InReplyTo);
        Assert.EndsWith("Looking at it now.\n\nEddie\nWebignite", draft.Body);
    }

    [Fact]
    public async Task A_new_draft_needs_a_mailbox_when_several_are_shared_and_valid_addresses()
    {
        Seed();
        var cancel = TestContext.Current.CancellationToken;
        var problem = await Assert.ThrowsAsync<AssistantException>(() => tools.BuildDraftAsync(null, null, false, ["a@b.co.za"], null, "Hi", "Text", cancel));
        Assert.Contains("accounts@webignite.co.za", problem.Message);
        await Assert.ThrowsAsync<AssistantException>(() => tools.BuildDraftAsync("accounts", null, false, ["nope"], null, "Hi", "Text", cancel));
        await Assert.ThrowsAsync<AssistantException>(() => tools.BuildDraftAsync("accounts", null, false, null, null, "Hi", "  ", cancel));

        var (account, draft) = await tools.BuildDraftAsync("accounts", null, false, ["Thandi <thandi@karoocoffee.co.za>"], null, "Statement", "Hi Thandi", cancel);
        Assert.Equal("accounts", account.Id);
        Assert.Equal("Hi Thandi", draft.Body);
    }

    [Fact]
    public void Drafts_saved_by_an_assistant_say_who_wrote_them()
    {
        var message = MailComposer.Build(new OutgoingMail { AccountId = "work", Body = "Hi" }, Work);
        message.Headers.Add(MailComposer.DraftedByHeader, "ChatGPT");
        Assert.Equal("ChatGPT", MailReader.Read(MailComposer.ToBytes(message), false).DraftedBy);
    }

    [Fact]
    public void Claude_settings_keep_everything_else_and_gain_the_connector()
    {
        var existing = """
            {
              // Claude adds comments sometimes
              "mcpServers": { "files": { "command": "npx", "args": ["files"] } },
              "theme": "dark",
            }
            """;
        var updated = ClaudeDesktopConfig.WithConnector(existing, @"C:\Webignite\Webignite.Connector.exe");
        var json = System.Text.Json.Nodes.JsonNode.Parse(updated)!;
        Assert.Equal("dark", json["theme"]!.GetValue<string>());
        Assert.Equal("npx", json["mcpServers"]!["files"]!["command"]!.GetValue<string>());
        Assert.True(ClaudeDesktopConfig.HasConnector(updated, @"C:\Webignite\Webignite.Connector.exe"));
        Assert.False(ClaudeDesktopConfig.HasConnector(updated, @"D:\Other\Webignite.Connector.exe"));

        Assert.True(ClaudeDesktopConfig.HasConnector(ClaudeDesktopConfig.WithConnector(null, "x.exe"), "x.exe"));
        Assert.Throws<FormatException>(() => ClaudeDesktopConfig.WithConnector("{ not json", "x.exe"));
    }

    [Fact]
    public void Client_mail_matches_their_addresses_and_their_own_domain_only()
    {
        var physio = new ClientMail(new Webignite.Core.Clients.Client
        {
            Id = "c", Name = "Fynbos Physio", Email = "admin@fynbosphysio.co.za; lerato.d@gmail.com", Website = "https://www.fynbosphysio.co.za/book",
        });
        Assert.Equal(["fynbosphysio.co.za"], physio.Domains);
        Assert.True(physio.Matches(new MailPerson(null, "desk@fynbosphysio.co.za")));
        Assert.True(physio.Matches(new MailPerson(null, "Lerato.D@gmail.com")));
        Assert.False(physio.Matches(new MailPerson(null, "someone.else@gmail.com")));
        Assert.True(physio.Matches(new MailSummary { AccountId = "a", Folder = "Sent", Uid = 1, From = new(null, "eddie@webignite.co.za"), To = [new(null, "admin@fynbosphysio.co.za")] }));

        var gmailOnly = new ClientMail(new Webignite.Core.Clients.Client { Id = "g", Name = "Joe", Email = "joe@gmail.com" });
        Assert.Empty(gmailOnly.Domains);
        Assert.True(new ClientMail(new Webignite.Core.Clients.Client { Id = "n", Name = "Nobody" }).IsEmpty);
        Assert.Equal("karoocoffee.co.za", ClientMail.WebsiteDomain("www.karoocoffee.co.za"));
    }

    private void Seed()
    {
        store.SaveMailAccount(Work);
        store.SaveMailAccount(Accounts);
        foreach (var id in new[] { "work", "accounts" })
            store.SaveMailFolder(new MailFolder { AccountId = id, Path = "INBOX", Name = "Inbox", Role = FolderRole.Inbox });
        var now = DateTimeOffset.Now;
        store.SaveMailSummaries(
        [
            new MailSummary { AccountId = "work", Folder = "INBOX", Uid = 41, Date = now, Subject = "Booking page", From = new("Lerato", "lerato@fynbosphysio.co.za"), Preview = "It just spins." },
            new MailSummary { AccountId = "work", Folder = "INBOX", Uid = 40, Date = now.AddDays(-1), Subject = "Renewal", From = new("xneelo", "noreply@xneelo.co.za"), Seen = true },
            new MailSummary { AccountId = "accounts", Folder = "INBOX", Uid = 1, Date = now, Subject = "Proof of payment INV-0107", From = new("Karoo Coffee Roasters", "accounts@karoocoffee.co.za"), Seen = true },
        ]);

        var message = new MimeMessage { Subject = "Booking page", MessageId = "booking@fynbosphysio.co.za" };
        message.From.Add(new MailboxAddress("Lerato", "lerato@fynbosphysio.co.za"));
        message.To.Add(new MailboxAddress(null, "eddie@webignite.co.za"));
        message.Body = new BodyBuilder { HtmlBody = "<p>Morning Eddie,</p><p>It just spins.</p>" }.ToMessageBody();
        store.SaveMailBody("work", "INBOX", 41, MailComposer.ToBytes(message));
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }
}
