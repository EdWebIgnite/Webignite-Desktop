using Webignite.Core.Mail;

namespace Webignite.Tests;

public sealed class NewMailTests
{
    private static MailSummary Mail(uint uid, bool seen = false, string from = "Lerato Dlamini", string subject = "Booking page") => new()
    {
        AccountId = "a1",
        Folder = "INBOX",
        Uid = uid,
        Seen = seen,
        Date = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero).AddMinutes(uid),
        From = new MailPerson(from, from.Replace(" ", ".").ToLowerInvariant() + "@example.co.za"),
        Subject = subject,
    };

    [Fact]
    public void First_look_at_a_mailbox_announces_nothing()
    {
        var result = NewMail.Find(new Dictionary<string, uint>(), [("a1", [Mail(1), Mail(2)])]);
        Assert.Empty(result.Fresh);
        Assert.Equal(2u, result.Announced["a1"]);
    }

    [Fact]
    public void Only_unread_mail_above_the_mark_is_new()
    {
        var result = NewMail.Find(new Dictionary<string, uint> { ["a1"] = 2 }, [("a1", [Mail(1), Mail(2), Mail(3), Mail(4, seen: true)])]);
        Assert.Equal(3u, Assert.Single(result.Fresh).Uid);
        Assert.Equal(4u, result.Announced["a1"]);
    }

    [Fact]
    public void Nothing_is_announced_twice()
    {
        var first = NewMail.Find(new Dictionary<string, uint> { ["a1"] = 2 }, [("a1", [Mail(3)])]);
        var second = NewMail.Find(first.Announced, [("a1", [Mail(3)])]);
        Assert.Single(first.Fresh);
        Assert.Empty(second.Fresh);
    }

    [Fact]
    public void An_empty_inbox_keeps_its_mark()
    {
        var result = NewMail.Find(new Dictionary<string, uint> { ["a1"] = 9 }, [("a1", [])]);
        Assert.Equal(9u, result.Announced["a1"]);
    }

    [Fact]
    public void Note_for_one_mail_names_sender_and_subject()
    {
        var (title, text) = NewMail.Note([Mail(3)]);
        Assert.Equal("Lerato Dlamini", title);
        Assert.Equal("Booking page", text);
    }

    [Fact]
    public void Note_for_several_counts_them()
    {
        var (title, text) = NewMail.Note([Mail(3), Mail(4, from: "Sipho Nkosi"), Mail(5)]);
        Assert.Equal("3 new mails", title);
        Assert.Equal("From Lerato Dlamini, Sipho Nkosi", text);
        var (_, many) = NewMail.Note([Mail(1, from: "A B"), Mail(2, from: "C D"), Mail(3, from: "E F"), Mail(4, from: "G H")]);
        Assert.EndsWith("and others", many);
    }

    [Fact]
    public void A_strange_interval_falls_back_to_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), NewMail.Interval(new MailCheckSettings { Minutes = 0 }));
        Assert.Equal(TimeSpan.FromMinutes(15), NewMail.Interval(new MailCheckSettings { Minutes = 15 }));
    }
}
