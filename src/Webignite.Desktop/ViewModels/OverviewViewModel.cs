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

public sealed partial class OverviewViewModel : ViewModelBase, IRefreshable, IDisposable
{
    private readonly AppServices services;

    public OverviewViewModel(AppServices services)
    {
        this.services = services;
        services.RepliesChanged += Refresh;
        Refresh();
    }

    public void Dispose() => services.RepliesChanged -= Refresh;

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

    /// <summary>How many Needs you lines show before "Show more": the most pressing come first.</summary>
    private const int FirstShown = 6;

    private List<SummaryLineViewModel> allSummary = [];
    private bool showAllSummary;

    /// <summary>The morning summary as shown: what needs doing, then what's good to know, the first few until asked for all.</summary>
    public ObservableCollection<SummaryLineViewModel> Summary { get; } = [];
    public bool HasSummary => Summary.Count > 0;

    /// <summary>"Show 7 more" under Needs you, or "Show fewer"; empty when everything fits.</summary>
    public string MoreSummaryText => allSummary.Count <= FirstShown ? "" : showAllSummary ? "Show fewer" : $"Show {allSummary.Count - FirstShown} more";
    public bool HasMoreSummary => MoreSummaryText.Length > 0;

    [RelayCommand]
    private void ToggleSummary()
    {
        showAllSummary = !showAllSummary;
        ShowSummary();
    }

    private void ShowSummary()
    {
        Summary.Clear();
        foreach (var line in showAllSummary ? allSummary : allSummary.Take(FirstShown))
            Summary.Add(line);
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(MoreSummaryText));
        OnPropertyChanged(nameof(HasMoreSummary));
    }
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

    /// <summary>What Webignite owes suppliers, from the bills in Xero, under the figure owed to you.</summary>
    public string BillsLine { get; private set; } = "";
    public bool HasBillsLine => BillsLine.Length > 0;

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
        var documents = services.Documents.List();

        Today.Clear();
        foreach (var item in Agenda.Build(today, today, services.Store.ListEvents(today, today), tasks, documents, services.Projects.Agenda(today, today, services.Me().Id)).Where(i => !i.Done))
            Today.Add(new AgendaItemViewModel(item));
        var overdueTasks = tasks.Count(t => TaskGroups.Of(t, today) == TaskGroup.Overdue);
        OverdueText = overdueTasks switch { 0 => "", 1 => "1 overdue to-do", _ => $"{overdueTasks} overdue to-dos" };

        Owed = Money.Rand(Receivables.Owed(documents));
        Overdue = Money.Rand(Receivables.Overdue(documents, today));
        QuotesOut = Money.Rand(Receivables.QuotesOut(documents, today));
        OwedLine = OverviewCards.Owed(documents);
        OverdueLine = OverviewCards.Overdue(documents, today);
        QuotesLine = OverviewCards.QuotesOut(documents, today);
        var bills = services.XeroBills.State;
        BillsLine = bills.Read is null ? ""
            : Bills.DueWithin(bills.Open, today, 7) is > 0 and var soon ? $"You owe {Money.Rand(soon)} in bills due this week. " + Bills.Summary(bills.Open, today)
            : Bills.Summary(bills.Open, today);

        var entries = services.Store.ListEntries(Dates.MonthStart(today).AddMonths(-5), today);
        CameIn = Money.Rand(Trends.ReceivedThisMonth(entries, today));
        CameInLine = Trends.AgainstLastMonth(entries, today) ?? "Add money received under Money to see it here.";
        CameInTrend = Trends.ReceivedByMonth(entries, today).Select(m => (double)m).ToList();
        PaymentsText = services.Payments.LastRun is { } checkedAt
            ? $"Payments from Xero, checked at {checkedAt.ToLocalTime():HH:mm}."
            : services.XeroApi is null ? "Connect Xero to see payments." : "Checking Xero for payments.";

        allSummary = [.. MorningSummary.Build(Summaries.Input(services, today, documents)).Select(line => new SummaryLineViewModel(line, services))];
        ShowSummary();

        OnPropertyChanged(string.Empty);
    }
}

/// <summary>One summary line; clicking it opens the page it's about, or drafts the reminder or follow-up.</summary>
public sealed partial class SummaryLineViewModel(SummaryLine line, AppServices services) : ViewModelBase
{
    public string Text => line.Text;
    public bool Problem => line.Problem;

    /// <summary>A mail waiting on a reply can be waved off: not everything needs one.</summary>
    public bool CanWaveOff => line.Target == SummaryTarget.Mail && line.Mail is not null;

    [RelayCommand]
    private void NoReplyNeeded()
    {
        if (line.Mail is { } mail)
            services.NoReplyNeeded(mail);
    }

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
                : await Webignite.Desktop.Chasing.DraftAsync(services, services.Documents.Get(line.Chase!.Stored.Id) ?? line.Chase.Stored);
            if (message is not null)
                services.ShowNote?.Invoke(problem ? "Couldn't draft it" : line.Target == SummaryTarget.Statement ? "Nothing owed" : "Nothing to chase", message, null);
        }
        finally
        {
            Busy = false;
        }
    }
}
