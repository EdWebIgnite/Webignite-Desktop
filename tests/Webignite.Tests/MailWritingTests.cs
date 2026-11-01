using System.Text;
using MimeKit;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Tests;

public sealed class MailWritingTests
{
    private static readonly MailAccount Eddie = new()
    {
        Id = "m1", DisplayName = "Eddie, Webignite", Address = "eddie@webignite.co.za",
        ImapHost = "mail.webignite.co.za", SmtpHost = "smtp.webignite.co.za", Signature = "Eddie\nWebignite",
    };

    private static readonly string[] Mine = ["eddie@webignite.co.za", "accounts@webignite.co.za"];

    [Fact]
    public void Recipients_are_read_with_names_and_bad_ones_are_listed()
    {
        var (people, bad) = Recipients.Parse("\"Mokoena, Thandi\" <thandi@karoocoffee.co.za>; pieter@bosveld.co.za, , not-an-address, PIETER@bosveld.co.za");
        Assert.Equal([new MailPerson("Mokoena, Thandi", "thandi@karoocoffee.co.za"), new MailPerson(null, "pieter@bosveld.co.za")], people);
        Assert.Equal(["not-an-address"], bad);
    }

    [Fact]
    public void Reply_goes_to_reply_to_and_reply_all_leaves_out_my_mailboxes()
    {
        var from = new MailPerson("Lerato", "lerato@fynbosphysio.co.za");
        MailPerson[] to = [new(null, "eddie@webignite.co.za"), new("Front desk", "desk@fynbosphysio.co.za")];
        MailPerson[] cc = [new(null, "accounts@webignite.co.za"), new(null, "lerato@fynbosphysio.co.za")];

        var reply = Replies.Recipients(from, [], to, cc, Mine, all: false);
        Assert.Equal([from], reply.To);
        Assert.Empty(reply.Cc);

        var all = Replies.Recipients(from, [new(null, "bookings@fynbosphysio.co.za")], to, cc, Mine, all: true);
        Assert.Equal(["bookings@fynbosphysio.co.za"], all.To.Select(p => p.Address));
        Assert.Equal(["desk@fynbosphysio.co.za", "lerato@fynbosphysio.co.za"], all.Cc.Select(p => p.Address));
    }

    [Fact]
    public void Replying_to_my_own_sent_mail_goes_to_the_people_I_wrote_to()
    {
        var reply = Replies.Recipients(new MailPerson(null, "eddie@webignite.co.za"), [], [new(null, "pieter@bosveld.co.za")], [], Mine, all: false);
        Assert.Equal(["pieter@bosveld.co.za"], reply.To.Select(p => p.Address));
    }

    [Fact]
    public void Sending_needs_a_recipient_and_valid_addresses()
    {
        var mail = new OutgoingMail { AccountId = "m1", Body = "Hi" };
        Assert.Contains("Add at least one person to send it to.", OutgoingRules.CheckForSending(mail, []));
        Assert.Single(OutgoingRules.CheckForSending(mail with { To = [new(null, "a@b.co.za")] }, ["oops"]));
        Assert.Empty(OutgoingRules.CheckForSending(mail with { To = [new(null, "a@b.co.za")] }, []));
        var huge = mail with { To = [new(null, "a@b.co.za")], Attachments = [new("big.zip", 30L * 1024 * 1024, FilePath: "x")] };
        Assert.Single(OutgoingRules.CheckForSending(huge, []));
    }

    [Fact]
    public void Mentioning_an_attachment_without_one_is_noticed()
    {
        var mail = new OutgoingMail { AccountId = "m1", Body = "Please see the attached invoice." };
        Assert.True(OutgoingRules.MentionsMissingAttachment(mail));
        Assert.False(OutgoingRules.MentionsMissingAttachment(mail with { Attachments = [new("inv.pdf", 10, FilePath: "inv.pdf")] }));
        Assert.False(OutgoingRules.MentionsMissingAttachment(mail with { Body = "Thanks, see you Friday." }));
    }

    [Fact]
    public void A_reply_quotes_the_original_keeps_the_thread_and_adds_the_signature()
    {
        var original = Original();
        var reply = MailComposer.Reply(original, Eddie, Mine, all: true);

        Assert.Equal("Re: Booking page", reply.Subject);
        Assert.Equal(["lerato@fynbosphysio.co.za"], reply.To.Select(p => p.Address));
        Assert.Equal(["desk@fynbosphysio.co.za"], reply.Cc.Select(p => p.Address));
        Assert.Equal("original@fynbosphysio.co.za", reply.InReplyTo);
        Assert.Equal(["first@fynbosphysio.co.za", "original@fynbosphysio.co.za"], reply.References);
        Assert.Equal("\n\nEddie\nWebignite", reply.Body);

        var message = MailComposer.Build(reply with { Body = "Looking at it now." + reply.Body }, Eddie);
        Assert.Equal("Eddie, Webignite", message.From.Mailboxes.Single().Name);
        Assert.Contains("> It just spins.", message.TextBody);
        Assert.StartsWith("Looking at it now.", message.TextBody);
        Assert.Contains("<blockquote", message.HtmlBody);
        Assert.Equal("original@fynbosphysio.co.za", message.InReplyTo);
    }

    [Fact]
    public void A_forward_carries_the_attachments_along()
    {
        var original = Original();
        var forward = MailComposer.Forward(original, Eddie);
        Assert.Equal("Fwd: Booking page", forward.Subject);
        Assert.Empty(forward.To);
        var attachment = Assert.Single(forward.Attachments);
        Assert.Equal("screenshot.png", attachment.FileName);

        var message = MailComposer.Build(forward with { To = [new(null, "dev@webignite.co.za")] }, Eddie, original);
        var carried = Assert.Single(message.Attachments.OfType<MimePart>());
        Assert.Equal("screenshot.png", carried.FileName);
        Assert.Contains("Forwarded message", message.TextBody);
    }

    [Fact]
    public void A_saved_draft_opens_again_with_the_typed_text_apart_from_the_quote()
    {
        var reply = MailComposer.Reply(Original(), Eddie, Mine, all: false);
        var typed = "Hi Lerato,\n\nOn it.\n\nOn Monday we'll test phones." + reply.Body;
        var draft = MailComposer.ToBytes(MailComposer.Build(reply with { Body = typed }, Eddie));

        var opened = MailComposer.FromDraft(draft, Eddie, uid: 7);
        Assert.Equal(typed.TrimEnd(), opened.Body);
        Assert.Equal(7u, opened.DraftUid);
        Assert.Equal(["lerato@fynbosphysio.co.za"], opened.To.Select(p => p.Address));
        Assert.NotNull(opened.Quote);
        Assert.Contains("It just spins.", opened.Quote!.Text);
        Assert.DoesNotContain("> ", opened.Quote.Text);
    }

    [Fact]
    public void A_draft_from_elsewhere_opens_as_one_block_of_text()
    {
        var message = new MimeMessage { Subject = "Re: Quote", Body = new TextPart("plain") { Text = "Hi Pieter,\n\nThanks.\n\nOn Mon, Pieter wrote:\n> Hello" } };
        message.To.Add(new MailboxAddress("Pieter", "pieter@bosveld.co.za"));
        var opened = MailComposer.FromDraft(MailComposer.ToBytes(message), Eddie, uid: 3);
        Assert.Null(opened.Quote);
        Assert.Contains("> Hello", opened.Body);
    }

    private static byte[] Original()
    {
        var builder = new BodyBuilder { TextBody = "Morning Eddie,\n\nIt just spins.", HtmlBody = "<p>Morning Eddie,</p><p>It just spins.</p>" };
        builder.Attachments.Add("screenshot.png", Encoding.ASCII.GetBytes("png"), new ContentType("image", "png"));
        var message = new MimeMessage { Subject = "Booking page", Body = builder.ToMessageBody(), MessageId = "original@fynbosphysio.co.za" };
        message.References.Add("first@fynbosphysio.co.za");
        message.From.Add(new MailboxAddress("Lerato", "lerato@fynbosphysio.co.za"));
        message.To.Add(new MailboxAddress(null, "eddie@webignite.co.za"));
        message.Cc.Add(new MailboxAddress(null, "desk@fynbosphysio.co.za"));
        message.Date = new DateTimeOffset(2026, 10, 31, 8, 20, 0, TimeSpan.FromHours(2));
        return MailComposer.ToBytes(message);
    }
}
