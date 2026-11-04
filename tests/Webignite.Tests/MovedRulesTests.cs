using Webignite.Core;
using Webignite.Core.Backups;
using Webignite.Core.Calendar;
using Webignite.Core.Finance;
using Webignite.Core.Mail;
using Webignite.Core.Time;
using Webignite.Core.Xero;

namespace Webignite.Tests;

/// <summary>Rules that used to sit in the pages, now in Core where they can be tested.</summary>
public sealed class MovedRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static readonly decimal[] ShortAmounts = [0m, 800m, 1500m, 12000m, 1_200_000m];

    private static StoredDocument Invoice(DocumentStatus status, Settlement? settledAs = null, int dueInDays = 10, DateTimeOffset? checkedAt = null) => new()
    {
        Id = StoredDocument.NewId(), Status = status, SettledAs = settledAs, Settled = settledAs is null ? null : DateTimeOffset.UnixEpoch,
        XeroId = status == DocumentStatus.InXero ? "x1" : null, XeroChecked = checkedAt, XeroStatus = status == DocumentStatus.InXero ? "AUTHORISED" : null,
        Document = Samples.Completed(Samples.NewInvoice() with { DueOrExpiryDate = Today.AddDays(dueInDays) }),
    };

    [Fact]
    public void A_document_row_says_where_it_stands_and_when_it_is_done()
    {
        Assert.Equal("Draft", Receivables.StatusText(Invoice(DocumentStatus.Draft), Today));
        Assert.Equal("To approve", Receivables.StatusText(Invoice(DocumentStatus.AwaitingApproval), Today));
        Assert.Equal("Overdue", Receivables.StatusText(Invoice(DocumentStatus.InXero, dueInDays: -3, checkedAt: DateTimeOffset.UnixEpoch), Today));
        Assert.True(Receivables.IsDone(Invoice(DocumentStatus.InXero, Settlement.Paid, checkedAt: DateTimeOffset.UnixEpoch), Today));
        Assert.True(Receivables.IsDone(Invoice(DocumentStatus.InXero), Today)); // in Xero, not read for payments yet
        Assert.False(Receivables.IsDone(Invoice(DocumentStatus.InXero, checkedAt: DateTimeOffset.UnixEpoch), Today));
        Assert.False(Receivables.IsDone(Invoice(DocumentStatus.InXero, Settlement.Voided, checkedAt: DateTimeOffset.UnixEpoch), Today));
    }

    [Fact]
    public void A_month_on_the_income_chart_says_how_it_went()
    {
        var month = new DateOnly(2026, 10, 1);
        Assert.Equal($"{Money.Rand(200m)} short", new ProjectionMonth(month, 1000m, 800m, IsPast: true, IsCurrent: false).GapText);
        Assert.True(new ProjectionMonth(month, 1000m, 800m, IsPast: true, IsCurrent: false).IsShort);
        Assert.Equal($"{Money.Rand(200m)} to come", new ProjectionMonth(month, 1000m, 800m, IsPast: false, IsCurrent: true).GapText);
        Assert.Equal($"{Money.Rand(100m)} more", new ProjectionMonth(month, 1000m, 1100m, IsPast: true, IsCurrent: false).GapText);
        Assert.Equal("On target", new ProjectionMonth(month, 1000m, 1000m, IsPast: true, IsCurrent: false).GapText);
        Assert.Equal("", new ProjectionMonth(month, 1000m, 0m, IsPast: false, IsCurrent: false).GapText);
        Assert.Equal("", new ProjectionMonth(month, null, 500m, IsPast: true, IsCurrent: false).GapText);
    }

    [Fact]
    public void Backups_are_a_problem_when_off_failing_or_late()
    {
        var now = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.FromHours(2));
        var fine = new BackupSettings { LastBackup = now.AddHours(-3) };
        Assert.False(BackupRules.IsProblem(fine, true, now));
        Assert.True(BackupRules.IsProblem(fine, false, now));
        Assert.True(BackupRules.IsProblem(fine with { LastProblem = "Disk full", LastProblemAt = now.AddHours(-1) }, true, now));
        Assert.False(BackupRules.IsProblem(fine with { LastProblem = "Disk full", LastProblemAt = now.AddHours(-5) }, true, now));
        Assert.True(BackupRules.IsProblem(new BackupSettings { LastBackup = now.AddDays(-3) }, true, now));
    }

    [Fact]
    public void Only_assistant_work_that_never_reached_xero_can_be_rejected()
    {
        var fromClaude = Invoice(DocumentStatus.AwaitingApproval);
        Assert.True(Approvals.CanReject(new WaitingItem(fromClaude, WaitingKind.FromAssistant, "")));
        Assert.False(Approvals.CanReject(new WaitingItem(fromClaude with { XeroId = "x9" }, WaitingKind.FromAssistant, "")));
        Assert.False(Approvals.CanReject(new WaitingItem(fromClaude, WaitingKind.XeroDraft, "")));
    }

    [Fact]
    public void Time_periods_and_who_can_be_invoiced()
    {
        Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8)), TimeRules.Range(TimeRules.Period.ThisWeek, Today));
        Assert.Equal((new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1)), TimeRules.Range(TimeRules.Period.LastWeek, Today));
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), TimeRules.Range(TimeRules.Period.ThisMonth, Today));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), TimeRules.Range(TimeRules.Period.LastMonth, Today));

        Webignite.Core.Clients.Client[] clients =
        [
            new() { Id = "1", Name = "Zulu Ltd", HourlyRate = 500m },
            new() { Id = "2", Name = "No rate" },
            new() { Id = "3", Name = "apex", Projects = [new() { Id = "p", Name = "Site", HourlyRate = 700m }] },
        ];
        Assert.Equal(["apex", "Zulu Ltd"], TimeRules.Invoiceable(clients).Select(c => c.Name));
    }

    [Fact]
    public void The_monthly_average_leaves_out_this_month_and_empty_months()
    {
        MonthTotals[] months = [new(2026, 9, 1000m, 0), new(2026, 10, 0, 0), new(2026, 11, 3000m, 0)];
        Assert.Equal($"On average {Money.Rand(1000m)} a month came in over the last 1 full month.", Ledger.AverageReceived(months));
        Assert.Equal("", Ledger.AverageReceived([new MonthTotals(2026, 11, 500m, 0)]));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    [InlineData(11, "11th")]
    [InlineData(31, "last day")]
    public void A_day_of_the_month(int day, string expected) => Assert.Equal(expected, Wording.DayOfMonth(day));

    [Theory]
    [InlineData("invoice.exe", true)]
    [InlineData("Setup.MSI", true)]
    [InlineData("statement.pdf.js", true)]
    [InlineData("statement.pdf", false)]
    [InlineData("photo.jpg", false)]
    public void Programs_are_never_opened_from_mail(string name, bool program) => Assert.Equal(program, AttachmentPreview.IsProgram(name));

    [Theory]
    [InlineData("14", 14)]
    [InlineData(" 0 ", 0)]
    [InlineData("365", 365)]
    [InlineData("366", null)]
    [InlineData("-1", null)]
    [InlineData("7.5", null)]
    [InlineData("", null)]
    public void Default_days_are_whole_numbers_up_to_a_year(string text, int? days) => Assert.Equal(days, DocumentDefaults.ParseDays(text));

    [Fact]
    public void Xero_settings_checks()
    {
        Assert.True(XeroRules.LooksLikeClientId("0123456789ABCDEF0123456789abcdef"));
        Assert.False(XeroRules.LooksLikeClientId("0123456789"));
        Assert.False(XeroRules.LooksLikeClientId("0123456789ABCDEF0123456789abcdeg"));
        Assert.True(XeroRules.HasNoVatRate(["OUTPUT", "NONE"]));
        Assert.False(XeroRules.HasNoVatRate(["OUTPUT"]));
    }

    [Fact]
    public void Which_mailbox_sends_what()
    {
        MailAccount Box(string id, bool invoices = false) => new() { Id = id, DisplayName = id, Address = id + "@webignite.co.za", ImapHost = "imap", SmtpHost = "smtp", SendsInvoices = invoices };
        Assert.Equal("accounts", MailAccounts.ForInvoices([Box("eddie"), Box("accounts", invoices: true)])!.Id);
        Assert.Null(MailAccounts.ForInvoices([Box("eddie")]));
        Assert.Equal("eddie", MailAccounts.ForEveryday([Box("accounts", invoices: true), Box("eddie")])!.Id);
        Assert.Equal("accounts", MailAccounts.ForEveryday([Box("accounts", invoices: true)])!.Id);
        Assert.Null(MailAccounts.ForEveryday([]));
    }

    [Fact]
    public void Sent_and_drafts_go_by_the_folder_role_not_its_name()
    {
        Assert.True(MailFolders.IsOutgoing(FolderRole.Sent));
        Assert.True(MailFolders.IsOutgoing(FolderRole.Drafts));
        Assert.False(MailFolders.IsOutgoing(FolderRole.Inbox));
        Assert.False(MailFolders.IsOutgoing(null));
    }

    [Theory]
    [InlineData(400, 1000)]
    [InlineData(1200, 2000)]
    [InlineData(2100, 2500)]
    [InlineData(37000, 50000)]
    public void The_chart_scale_tops_out_at_a_round_figure(decimal max, decimal top) => Assert.Equal(top, ChartScale.NiceTop(max));

    [Fact]
    public void Chart_labels_are_short() =>
        Assert.Equal(["R 0", "R 800", "R 1.5k", "R 12k", "R 1.2m"], ShortAmounts.Select(ChartScale.Short));

    [Theory]
    [InlineData("9", 9, 0)]
    [InlineData("9:30", 9, 30)]
    [InlineData("14:00", 14, 0)]
    [InlineData("9h30", 9, 30)]
    [InlineData("9.30", 9, 30)]
    public void Event_times_are_read_as_people_type_them(string text, int hour, int minute)
    {
        Assert.True(CalendarEvents.TryParseTime(text, out var time));
        Assert.Equal(new TimeOnly(hour, minute), time);
    }

    [Theory]
    [InlineData("25")]
    [InlineData("lunch")]
    [InlineData("")]
    public void Not_a_time(string text) => Assert.False(CalendarEvents.TryParseTime(text, out _));
}
