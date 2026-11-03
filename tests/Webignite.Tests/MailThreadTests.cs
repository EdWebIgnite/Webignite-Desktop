using Webignite.Core.Mail;

namespace Webignite.Tests;

/// <summary>Conversations: replies grouped under the first mail.</summary>
public sealed class MailThreadTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(2));
    private static uint uid;

    private static MailSummary Mail(string subject, int hours, string? id = null, string? inReplyTo = null, string account = "work", bool seen = true) => new()
    {
        AccountId = account,
        Folder = "INBOX",
        Uid = ++uid,
        Subject = subject,
        Date = Start.AddHours(hours),
        MessageId = id,
        InReplyTo = inReplyTo,
        Seen = seen,
        From = new MailPerson("Pieter van Wyk", "pieter@bosveldbuilders.co.za"),
    };

    [Theory]
    [InlineData("Re: Website quote", "website quote")]
    [InlineData("RE: Fwd: re:  Website   Quote", "website quote")]
    [InlineData("AW: Angebot", "angebot")]
    [InlineData("Reminder", "reminder")]
    [InlineData(null, "")]
    public void Topic_drops_reply_and_forward_prefixes(string? subject, string expected) =>
        Assert.Equal(expected, MailThreads.Topic(subject));

    [Fact]
    public void A_reply_joins_the_mail_it_answers_and_the_newest_conversation_comes_first()
    {
        var quote = Mail("Website quote", 0, id: "<q1@webignite>");
        var reply = Mail("Re: Website quote", 3, id: "<r1@bosveld>", inReplyTo: "<q1@webignite>", seen: false);
        var other = Mail("Hosting renewal", 1);
        var threads = MailThreads.Group([reply, other, quote]);

        Assert.Equal(2, threads.Count);
        Assert.Equal([quote, reply], threads[0].Messages);
        Assert.Equal(reply, threads[0].Latest);
        Assert.True(threads[0].AnyUnread);
        Assert.Equal([other], threads[1].Messages);
    }

    [Fact]
    public void A_reply_without_in_reply_to_joins_by_subject()
    {
        var first = Mail("Logo files", 0);
        var reply = Mail("RE: Logo files", 2);
        Assert.Single(MailThreads.Group([first, reply]));
    }

    [Fact]
    public void Two_mails_with_the_same_subject_and_no_reply_prefix_stay_apart()
    {
        Assert.Equal(2, MailThreads.Group([Mail("Invoice", 0), Mail("Invoice", 5)]).Count);
    }

    [Fact]
    public void Mail_in_different_mailboxes_never_joins()
    {
        var first = Mail("Logo files", 0, account: "work");
        var reply = Mail("Re: Logo files", 2, account: "accounts");
        Assert.Equal(2, MailThreads.Group([first, reply]).Count);
    }

    [Fact]
    public void A_chain_of_replies_is_one_conversation()
    {
        var a = Mail("Booking page", 0, id: "<a>");
        var b = Mail("Re: Booking page", 1, id: "<b>", inReplyTo: "<a>");
        var c = Mail("Re: Re: Booking page", 2, id: "<c>", inReplyTo: "<b>");
        var thread = Assert.Single(MailThreads.Group([c, a, b]));
        Assert.Equal(3, thread.Count);
        Assert.Equal(a, thread.First);
    }

    [Fact]
    public void Conversations_are_switched_off_per_list()
    {
        var settings = new MailViewSettings();
        Assert.True(settings.ConversationsIn("work|INBOX"));
        settings = settings.WithConversations("work|INBOX", false);
        Assert.False(settings.ConversationsIn("work|INBOX"));
        Assert.True(settings.ConversationsIn("work|Sent"));
        Assert.True(settings.WithConversations("work|INBOX", true).ConversationsIn("work|INBOX"));
        Assert.Equal(3, MailThreads.Single([Mail("Re: x", 0), Mail("Re: x", 1), Mail("y", 2)]).Count);
    }
}

/// <summary>Which client a mail row belongs to, for its badge and the client filter.</summary>
public sealed class MailClientTests
{
    private static readonly Webignite.Core.Clients.Client Karoo = new() { Id = "c1", Name = "Karoo Coffee Roasters", Email = "thandi@karoocoffee.co.za" };
    private static readonly Webignite.Core.Clients.Client Fynbos = new() { Id = "c2", Name = "Fynbos Physio", Website = "https://fynbosphysio.co.za" };
    private static readonly Webignite.Core.Clients.Client NoAddress = new() { Id = "c3", Name = "Bosveld Builders" };

    private static MailSummary Mail(string from, string to = "eddie@webignite.co.za") => new()
    {
        AccountId = "work", Folder = "INBOX", Uid = 1, From = new MailPerson(null, from), To = [new MailPerson(null, to)],
    };

    [Fact]
    public void The_sender_decides_the_client_then_who_it_went_to()
    {
        var find = ClientMail.Finder([NoAddress, Karoo, Fynbos]);
        Assert.Equal(Karoo, find(Mail("accounts@karoocoffee.co.za")));
        Assert.Equal(Fynbos, find(Mail("lerato@fynbosphysio.co.za")));
        Assert.Equal(Karoo, find(Mail("eddie@webignite.co.za", to: "thandi@karoocoffee.co.za")));
        Assert.Null(find(Mail("noreply@xneelo.co.za")));
    }

    [Fact]
    public void No_clients_with_addresses_means_no_badges()
    {
        Assert.Null(ClientMail.Finder([NoAddress])(Mail("someone@bosveld.co.za")));
    }
}

/// <summary>Which attachments open inside the app.</summary>
public sealed class AttachmentPreviewTests
{
    [Theory]
    [InlineData("Invoice INV-0107.pdf", "application/pdf", 50_000, PreviewKind.Pdf)]
    [InlineData("scan", "application/pdf", 50_000, PreviewKind.Pdf)]
    [InlineData("booking-phone-1.PNG", "image/png", 48_000, PreviewKind.Picture)]
    [InlineData("photo.jpeg", null, 2_000_000, PreviewKind.Picture)]
    [InlineData("logo.svg", "image/svg+xml", 4_000, PreviewKind.None)]
    [InlineData("contract.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 30_000, PreviewKind.None)]
    [InlineData("huge.pdf", "application/pdf", 40_000_000, PreviewKind.None)]
    public void Pdfs_and_pictures_preview_in_the_app(string name, string? type, long size, PreviewKind expected) =>
        Assert.Equal(expected, AttachmentPreview.Kind(name, type, size));
}
