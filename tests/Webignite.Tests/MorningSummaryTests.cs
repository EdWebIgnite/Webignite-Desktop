using Webignite.Core.Clients;
using Webignite.Core.Finance;
using Webignite.Core.Mail;
using Webignite.Core.Overview;
using Webignite.Core.Renewals;

namespace Webignite.Tests;

public sealed class MorningSummaryTests
{
    // Sunday 1 November 2026
    private static readonly DateOnly Today = new(2026, 11, 1);

    private static readonly Client Fynbos = new() { Id = "c1", Name = "Fynbos Physio", Email = "lerato@fynbosphysio.co.za" };
    private static readonly Client Karoo = new() { Id = "c2", Name = "Karoo Coffee Roasters", Email = "karoocoffee@gmail.com" };

    private static MailSummary Mail(string address, string name, int daysAgo, bool answered = false, string subject = "Booking page") => new()
    {
        AccountId = "a1",
        Folder = "INBOX",
        Uid = (uint)(100 - daysAgo),
        From = new MailPerson(name, address),
        Date = new DateTimeOffset(Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(9, 0))),
        Answered = answered,
        Subject = subject,
    };

    private static SummaryInput Input(params MailSummary[] inbox) => new()
    {
        Today = Today,
        Inbox = inbox,
        Clients = [Fynbos, Karoo],
        OwnAddresses = ["eddie@webignite.co.za", "accounts@webignite.co.za"],
    };

    [Fact]
    public void Client_mail_without_a_reply_is_listed()
    {
        var line = Assert.Single(MorningSummary.WaitingOnReply(Input(Mail("lerato@fynbosphysio.co.za", "Lerato Dlamini", 3))));
        Assert.Equal("Lerato Dlamini (Fynbos Physio) is waiting on a reply since Thursday: Booking page", line.Text);
        Assert.Equal(SummaryTarget.Mail, line.Target);
        Assert.NotNull(line.Mail);
    }

    [Fact]
    public void Colleagues_at_the_client_count_by_domain()
    {
        var line = Assert.Single(MorningSummary.WaitingOnReply(Input(Mail("reception@fynbosphysio.co.za", "Reception", 0))));
        Assert.StartsWith("Reception (Fynbos Physio) is waiting on a reply since today", line.Text);
    }

    [Fact]
    public void Gmail_doesnt_make_everyone_a_client()
    {
        Assert.Empty(MorningSummary.WaitingOnReply(Input(Mail("someone@gmail.com", "Someone", 1))));
        Assert.Single(MorningSummary.WaitingOnReply(Input(Mail("karoocoffee@gmail.com", "Karoo Coffee Roasters", 1))));
    }

    [Fact]
    public void Answered_old_and_own_mail_is_left_out()
    {
        Assert.Empty(MorningSummary.WaitingOnReply(Input(
            Mail("lerato@fynbosphysio.co.za", "Lerato", 1, answered: true),
            Mail("lerato@fynbosphysio.co.za", "Lerato", 20),
            Mail("eddie@webignite.co.za", "Eddie", 0))));
    }

    [Fact]
    public void One_line_per_person_for_their_newest_mail()
    {
        var line = Assert.Single(MorningSummary.WaitingOnReply(Input(
            Mail("lerato@fynbosphysio.co.za", "Lerato", 4, subject: "Older"),
            Mail("lerato@fynbosphysio.co.za", "Lerato", 1, subject: "Newer"))));
        Assert.EndsWith("Newer", line.Text);
    }

    [Fact]
    public void Problems_come_first_and_show_in_red()
    {
        var overdueInvoice = new StoredDocument
        {
            Id = "d1",
            Status = DocumentStatus.InXero,
            XeroId = "x1",
            XeroNumber = "INV-0111",
            Document = Samples.Completed(Samples.NewInvoice()) with { DueOrExpiryDate = Today.AddDays(-5) },
        };
        var lines = MorningSummary.Build(Input(Mail("lerato@fynbosphysio.co.za", "Lerato", 1)) with
        {
            BackupsOn = false,
            Documents = [overdueInvoice],
            Renewals =
            [
                new Renewal { Id = "r1", Name = "karoocoffee.co.za", ClientName = "Karoo Coffee Roasters", RenewsOn = Today.AddDays(4) },
                new Renewal { Id = "r2", Name = "webignite.co.za", RenewsOn = Today.AddDays(-1) },
                new Renewal { Id = "r3", Name = "far-away.co.za", RenewsOn = Today.AddDays(60) },
            ],
            Entries =
            [
                new MoneyEntry { Id = "e1", Kind = EntryKind.Received, Date = Today, Amount = 2800m },
                new MoneyEntry { Id = "e2", Kind = EntryKind.Received, Date = Today.AddDays(-3), Amount = 9999m },
                new MoneyEntry { Id = "e3", Kind = EntryKind.Expense, Date = Today, Amount = 100m },
            ],
        });

        Assert.Equal(
        [
            "Backups are off. Choose a backup password in Settings.",
            $"INV-0111 for Karoo Coffee Roasters is 5 days overdue ({Money.Rand(450m)}).",
            "webignite.co.za was due for renewal yesterday.",
            "Lerato (Fynbos Physio) is waiting on a reply since yesterday: Booking page",
            "karoocoffee.co.za for Karoo Coffee Roasters renews on Thursday.",
            $"{Money.Rand(2800m)} has come in so far in November.",
        ], lines.Select(l => l.Text));
        Assert.Equal([true, true, true, false, false, false], lines.Select(l => l.Problem));
    }

    [Fact]
    public void A_quiet_day_has_nothing()
    {
        Assert.Empty(MorningSummary.Build(new SummaryInput { Today = Today }));
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "yesterday")]
    [InlineData(-1, "tomorrow")]
    [InlineData(3, "on Thursday")]
    [InlineData(-4, "on Thursday")]
    [InlineData(10, "on 22 October")]
    public void Days_read_naturally(int daysAgo, string expected) =>
        Assert.Equal(expected, MorningSummary.On(Today.AddDays(-daysAgo), Today));
}
