namespace Webignite.Tests;

public sealed class ChaserTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);
    private static DateTimeOffset DaysAgo(int days) => new(Today.AddDays(-days).ToDateTime(new TimeOnly(10, 0)));

    private static StoredDocument Invoice(int dueDaysAgo, DateTimeOffset? chased = null) => new()
    {
        Id = "i" + dueDaysAgo,
        Status = DocumentStatus.InXero,
        Document = Samples.Completed(Samples.NewInvoice()) with { DueOrExpiryDate = Today.AddDays(-dueDaysAgo) },
        Xero = new()
        {
            Id = "x",
            Number = "INV-0111",
        },
        Delivery = new() { Chased = chased },
    };

    private static StoredDocument Quote(int emailedDaysAgo, int expiresIn = 20, DateTimeOffset? chased = null) => new()
    {
        Id = "q" + emailedDaysAgo,
        Status = DocumentStatus.InXero,
        Document = Samples.Completed(Samples.NewInvoice()) with { Kind = DocumentKind.Quote, DueOrExpiryDate = Today.AddDays(expiresIn) },
        Xero = new()
        {
            Id = "x",
            Number = "QU-0012",
        },
        Delivery = new()
        {
            Emailed = DaysAgo(emailedDaysAgo),
            Chased = chased,
        },
    };

    [Fact]
    public void Overdue_invoices_need_a_reminder()
    {
        var item = Assert.Single(Chasers.Find([Invoice(5), Invoice(-3)], Today));
        Assert.Equal(ChaseKind.Reminder, item.Kind);
        Assert.Equal(5, item.Days);
        Assert.True(item.Due);
    }

    [Fact]
    public void Reminders_wait_a_week_between()
    {
        Assert.False(Chasers.Find([Invoice(10, chased: DaysAgo(3))], Today)[0].Due);
        Assert.True(Chasers.Find([Invoice(10, chased: DaysAgo(7))], Today)[0].Due);
    }

    [Fact]
    public void Quotes_get_one_follow_up_after_a_week()
    {
        Assert.Empty(Chasers.Find([Quote(6)], Today));
        Assert.Equal(ChaseKind.FollowUp, Assert.Single(Chasers.Find([Quote(7)], Today)).Kind);
        Assert.Empty(Chasers.Find([Quote(9, chased: DaysAgo(1))], Today));
        Assert.Empty(Chasers.Find([Quote(9, expiresIn: -1)], Today));
    }

    [Fact]
    public void Settled_and_unsent_documents_are_left_alone()
    {
        Assert.Empty(Chasers.Find([Invoice(5) with { Settled = new SettledState { On = DaysAgo(1), As = Settlement.Paid } }], Today));
        Assert.Empty(Chasers.Find([Invoice(5).WithXero(x => x with { Id = null })], Today));
        Assert.Empty(Chasers.Find([Quote(9).WithDelivery(x => x with { Emailed = null })], Today));
    }

    [Theory]
    [InlineData(DocumentKind.Invoice, "PAID", 0, "paid")]
    [InlineData(DocumentKind.Invoice, "AUTHORISED", 0, "paid")]
    [InlineData(DocumentKind.Invoice, "AUTHORISED", 100, null)]
    [InlineData(DocumentKind.Quote, "ACCEPTED", null, "accepted")]
    [InlineData(DocumentKind.Quote, "SENT", null, null)]
    public void What_Xero_says_settles_it(DocumentKind kind, string status, int? due, string? settled) =>
        Assert.Equal(settled, Chasers.SettledBy(kind, status, due) is { } how ? Settlements.Word(how) : null);

    [Fact]
    public void Reminder_wording_fills_days_overdue()
    {
        var filled = DocumentEmail.Fill(new EmailTemplates().Reminder, new DocumentEmailValues
        {
            Kind = DocumentKind.Invoice,
            ContactName = "Karoo Coffee Roasters",
            FirstName = "Thandi",
            Number = "INV-0111",
            Total = 2725m,
            AmountDue = 2725m,
            DueOrExpiryDate = new DateOnly(2026, 10, 20),
            OnlineLink = "https://in.xero.com/abc",
            TradingName = "Webignite",
            DaysOverdue = 12,
        });
        Assert.Empty(filled.Unfilled);
        Assert.Equal("Reminder: invoice INV-0111 is overdue", filled.Subject);
        Assert.Contains("was due on 20 October 2026, and is now 12 days overdue.", filled.Body);
        Assert.Contains("Hi Thandi,", filled.Body);
    }

    [Fact]
    public void Follow_up_wording_fills()
    {
        var filled = DocumentEmail.Fill(new EmailTemplates().FollowUp, new DocumentEmailValues
        {
            Kind = DocumentKind.Quote,
            ContactName = "Bosveld Builders",
            Number = "QU-0012",
            Total = 17700m,
            DueOrExpiryDate = new DateOnly(2026, 11, 30),
            TradingName = "Webignite",
        });
        Assert.Empty(filled.Unfilled);
        Assert.StartsWith("Hi Bosveld Builders,", filled.Body);
        Assert.Contains("valid until 30 November 2026", filled.Body);
    }
}
