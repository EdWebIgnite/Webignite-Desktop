using Webignite.Core;
using Webignite.Core.Finance;
using Webignite.Core.Mail;
using Webignite.Core.Retainers;
using Webignite.Core.Xero;

namespace Webignite.Tests;

/// <summary>Rules the code review found with no test.</summary>
public sealed class MissingRuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    [Fact]
    public void Monthly_totals_count_only_what_is_running_this_month()
    {
        RecurringExpense Cost(decimal amount, bool paused = false, DateOnly? ends = null) => new()
        {
            Id = RecurringExpense.NewId(), Name = "xneelo", Amount = amount, DayOfMonth = 1, Starts = new DateOnly(2026, 1, 1), Paused = paused, Ends = ends,
        };
        Assert.Equal(300m, RecurringExpenses.MonthlyTotal(
            [Cost(100m), Cost(200m, ends: new DateOnly(2026, 11, 30)), Cost(400m, paused: true), Cost(800m, ends: new DateOnly(2026, 10, 31))], Today));

        Retainer Monthly(decimal price, bool paused = false) => new()
        {
            Id = Retainer.NewId(), ClientName = "Karoo", Title = "SEO", DayOfMonth = 1, Starts = new DateOnly(2026, 1, 1), Paused = paused,
            Lines = [new Line { Description = "SEO", Quantity = 1, UnitPrice = price }],
        };
        Assert.Equal(1500m, RetainerSchedule.MonthlyTotal([Monthly(1500m), Monthly(900m, paused: true)], Today));
    }

    [Fact]
    public void The_monthly_schedule()
    {
        Assert.Equal(new DateOnly(2026, 2, 28), MonthlySchedule.DateIn(31, new DateOnly(2026, 2, 1)));
        Assert.Equal(
            [new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1)],
            MonthlySchedule.MonthsSoFar(new DateOnly(2026, 9, 15), null, Today));
        Assert.Equal([new DateOnly(2026, 9, 1)], MonthlySchedule.MonthsSoFar(new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 30), Today));
        Assert.Equal(new DateOnly(2026, 11, 1), MonthlySchedule.ResumeFrom(new DateOnly(2026, 3, 1), Today));
        Assert.True(MonthlySchedule.EndsBeforeStart(new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 30)));
        Assert.False(MonthlySchedule.EndsBeforeStart(new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 1)));
    }

    [Theory]
    [InlineData("REVENUE", "ACTIVE", true)]
    [InlineData("SALES", "ACTIVE", true)]
    [InlineData("OTHERINCOME", "ACTIVE", true)]
    [InlineData("REVENUE", "ARCHIVED", false)]
    [InlineData("EXPENSE", "ACTIVE", false)]
    public void Which_xero_accounts_take_sales(string type, string status, bool sales) => Assert.Equal(sales, XeroRules.IsSalesAccount(type, status));

    [Fact]
    public void A_discount_bigger_than_the_subtotal_and_payments_that_dont_add_up_are_caught()
    {
        var doc = Samples.Completed(Samples.NewInvoice());
        Assert.Contains("The document discount is more than the subtotal.",
            DocumentValidator.Validate(doc with { DocumentDiscountAmount = doc.Subtotal + 1 }));
        Assert.Contains("The amount paid and the balance due don't add up to the total.",
            DocumentValidator.Validate(doc with { AmountPaid = 100m, BalanceDue = 100m }));
        Assert.DoesNotContain("The amount paid and the balance due don't add up to the total.",
            DocumentValidator.Validate(doc with { AmountPaid = 100m, BalanceDue = doc.Total - 100m }));
    }

    [Fact]
    public void Every_offered_placeholder_is_filled()
    {
        var values = new DocumentEmailValues
        {
            Kind = DocumentKind.Invoice, ContactName = "Karoo Coffee Roasters", FirstName = "Thandi", Number = "INV-0111", Total = 2725m,
            AmountDue = 2725m, DueOrExpiryDate = Today, Reference = "Website", OnlineLink = "https://in.xero.com/abc", TradingName = "Webignite",
            DaysOverdue = 3,
        };
        foreach (var kind in new[] { DocumentKind.Invoice, DocumentKind.Quote })
        {
            var placeholders = DocumentEmail.Placeholders(kind);
            var filled = DocumentEmail.Fill(new EmailTemplate("x", string.Join(" ", placeholders)), values with { Kind = kind });
            Assert.Empty(filled.Unfilled);
        }
    }

    [Theory]
    [InlineData("AW: Angebot", "AW: Angebot")]
    [InlineData("SV: Faktura", "SV: Faktura")]
    [InlineData("Website quote", "Re: Website quote")]
    public void A_reply_in_another_language_isnt_prefixed_twice(string subject, string expected) =>
        Assert.Equal(expected, MailText.ReplySubject(subject));

    [Fact]
    public void New_mail_uses_the_same_mark_as_mail_rules()
    {
        MailSummary Mail(uint uid, bool seen = false) => new() { AccountId = "work", Folder = "INBOX", Uid = uid, Seen = seen };
        var first = NewMail.Find(new Dictionary<string, uint>(), [("work", [Mail(1), Mail(2)])]);
        Assert.Empty(first.Fresh);
        var next = NewMail.Find(first.Announced, [("work", [Mail(1), Mail(2), Mail(3), Mail(4, seen: true)])]);
        Assert.Equal([3u], next.Fresh.Select(m => m.Uid));
        Assert.Equal(4u, next.Announced["work"]);
    }
}

public sealed class DraftNoteTests
{
    [Fact]
    public void The_note_above_a_prepared_mail_says_what_needs_a_look()
    {
        Assert.Null(Webignite.Core.Mail.DraftNotes.For(1, "No address.", []));
        Assert.Equal("No address.", Webignite.Core.Mail.DraftNotes.For(0, "No address.", []));
        Assert.Equal("Couldn't fill [Due Date]. Fix the text before sending. 2 notes are empty.",
            Webignite.Core.Mail.DraftNotes.For(1, "No address.", ["[Due Date]"], " 2 notes are empty. "));
    }
}
