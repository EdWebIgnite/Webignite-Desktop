using System.Collections.ObjectModel;
using Webignite.Core.Calendar;
using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Core.Tasks;
using Webignite.Core.Backups;
using Webignite.Core.Mail;
using Webignite.Core.Overview;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core;

namespace Webignite.Desktop.ViewModels;

public sealed partial class OverviewViewModel : ViewModelBase, IRefreshable
{
    private readonly AppServices services;

    public OverviewViewModel(AppServices services)
    {
        this.services = services;
        Refresh();
    }

    public string Greeting
    {
        get
        {
            var hour = DateTime.Now.Hour;
            return hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
        }
    }

    public string TodayText => DateTime.Today.ToString("dddd, d MMMM");

    public ObservableCollection<AgendaItemViewModel> Today { get; } = [];

    /// <summary>The morning summary: what needs doing, then what's good to know.</summary>
    public ObservableCollection<SummaryLineViewModel> Summary { get; } = [];
    public bool HasSummary => Summary.Count > 0;
    public bool TodayIsEmpty => Today.Count == 0;
    public string OverdueText { get; private set; } = "";
    public bool HasOverdue => OverdueText.Length > 0;
    public string Owed { get; private set; } = "";
    public string Overdue { get; private set; } = "";
    public string QuotesOut { get; private set; } = "";
    public string PaymentsText { get; private set; } = "";

    /// <summary>What each figure is made of, e.g. "2 invoices, the oldest 14 days late."</summary>
    public string OwedLine { get; private set; } = "";
    public string OverdueLine { get; private set; } = "";
    public string QuotesLine { get; private set; } = "";

    /// <summary>Money received this month so far, from the Money page.</summary>
    public string CameIn { get; private set; } = "";

    /// <summary>"R 2 800.00 more than by 3 Oct.", or a plain line when there is nothing to compare.</summary>
    public string CameInLine { get; private set; } = "";

    /// <summary>Received in each of the last six months, this one so far last, for the trend line.</summary>
    public IReadOnlyList<double> CameInTrend { get; private set; } = [];
    public bool XeroConnected => services.XeroApi is not null;

    /// <summary>Set by the main window: reads payments from Xero now.</summary>
    public Func<Task>? CheckPayments { get; set; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial bool CheckingPayments { get; set; }

    [RelayCommand]
    private async Task CheckPaymentsNow()
    {
        if (CheckPayments is null || CheckingPayments)
            return;
        CheckingPayments = true;
        try
        {
            await CheckPayments();
        }
        finally
        {
            CheckingPayments = false;
        }
    }

    public void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        services.Store.RaiseAutomaticTasks(services.Settings.Tasks, today);
        var tasks = services.Store.ListTasks();
        var documents = services.Store.List();

        Today.Clear();
        foreach (var item in Agenda.Build(today, today, services.Store.ListEvents(today, today), tasks, documents).Where(i => !i.Done))
            Today.Add(new AgendaItemViewModel(item));
        var overdueTasks = tasks.Count(t => TaskGroups.Of(t, today) == TaskGroup.Overdue);
        OverdueText = overdueTasks switch { 0 => "", 1 => "1 overdue to-do", _ => $"{overdueTasks} overdue to-dos" };

        Owed = Money.Rand(Receivables.Owed(documents));
        Overdue = Money.Rand(Receivables.Overdue(documents, today));
        QuotesOut = Money.Rand(Receivables.QuotesOut(documents, today));
        OwedLine = OverviewCards.Owed(documents);
        OverdueLine = OverviewCards.Overdue(documents, today);
        QuotesLine = OverviewCards.QuotesOut(documents, today);

        var entries = services.Store.ListEntries(Dates.MonthStart(today).AddMonths(-5), today);
        CameIn = Money.Rand(Trends.ReceivedThisMonth(entries, today));
        CameInLine = Trends.AgainstLastMonth(entries, today) ?? "Add money received under Money to see it here.";
        CameInTrend = Trends.ReceivedByMonth(entries, today).Select(m => (double)m).ToList();
        PaymentsText = services.Payments.LastRun is { } checkedAt
            ? $"Payments from Xero, checked at {checkedAt.ToLocalTime():HH:mm}."
            : services.XeroApi is null ? "Connect Xero to see payments." : "Checking Xero for payments.";

        Summary.Clear();
        foreach (var line in MorningSummary.Build(SummaryInput(today, documents)))
            Summary.Add(new SummaryLineViewModel(line, services));

        OnPropertyChanged(string.Empty);
    }

    private SummaryInput SummaryInput(DateOnly today, IReadOnlyList<StoredDocument> documents)
    {
        var accounts = services.Store.ListMailAccounts();
        var inbox = new List<MailSummary>();
        foreach (var account in accounts)
        {
            if (services.Store.ListMailFolders(account.Id).FirstOrDefault(f => f.Role == FolderRole.Inbox) is { } folder)
                inbox.AddRange(services.Store.ListMailSummaries(account.Id, folder.Path, 300));
        }
        var hasPassword = services.HasBackupPassword;
        return new SummaryInput
        {
            Today = today,
            Documents = documents,
            Renewals = services.Store.ListRenewals(),
            Entries = services.Store.ListEntries(Dates.MonthStart(today), today),
            Inbox = inbox,
            Clients = services.Store.ListClients(),
            OwnAddresses = accounts.Select(a => a.Address).ToList(),
            StatementsSent = services.Settings.StatementsSent,
            Sites = Webignite.Core.Web.Uptime.Domains(services.Store.ListClients(), services.Settings.Uptime)
                .Select(d => services.Settings.Uptime.Sites.GetValueOrDefault(d.Domain)).OfType<Webignite.Core.Web.WatchedSite>().ToList(),
            BackupsOn = hasPassword,
            BackupOverdue = BackupRules.Overdue(services.Settings.Backups, hasPassword, DateTimeOffset.Now),
        };
    }
}

/// <summary>One summary line; clicking it opens the page it's about, or drafts the reminder or follow-up.</summary>
public sealed partial class SummaryLineViewModel(SummaryLine line, AppServices services) : ViewModelBase
{
    public string Text => line.Text;
    public bool Problem => line.Problem;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial bool Busy { get; set; }

    public string Action => line.Target switch
    {
        SummaryTarget.Chase => line.Chase!.Kind == Webignite.Core.Documents.ChaseKind.Reminder ? "Draft reminder" : "Draft follow-up",
        SummaryTarget.Statement => "Draft statement",
        SummaryTarget.Mail => "Open mail",
        SummaryTarget.Approvals => "Review",
        SummaryTarget.Invoices => "Invoices",
        SummaryTarget.Renewals => "Renewals",
        SummaryTarget.Money => "Money",
        SummaryTarget.Clients => line.Client is null ? "Clients" : "Open client",
        SummaryTarget.SiteCheck => "Site check",
        _ => "Settings",
    };

    [RelayCommand]
    private async Task Open()
    {
        if (line.Target == SummaryTarget.Clients && line.Client is { } client)
        {
            services.Go.Client(client);
            return;
        }
        if (line.Target is not (SummaryTarget.Chase or SummaryTarget.Statement))
        {
            services.Go.Summary(line.Target, line.Mail);
            return;
        }
        Busy = true;
        try
        {
            var (message, problem) = line.Target == SummaryTarget.Statement
                ? await Webignite.Desktop.Statementing.DraftAsync(services, line.Customer!)
                : await Webignite.Desktop.Chasing.DraftAsync(services, services.Store.Get(line.Chase!.Stored.Id) ?? line.Chase.Stored);
            if (message is not null)
                services.ShowNote?.Invoke(problem ? "Couldn't draft it" : line.Target == SummaryTarget.Statement ? "Nothing owed" : "Nothing to chase", message, null);
        }
        finally
        {
            Busy = false;
        }
    }
}
