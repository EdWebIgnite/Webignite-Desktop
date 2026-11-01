using System.Collections.ObjectModel;
using Webignite.Core.Calendar;
using Webignite.Core.Documents;
using Webignite.Core.Tasks;

namespace Webignite.Desktop.ViewModels;

public sealed class OverviewViewModel : ViewModelBase
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
    public bool TodayIsEmpty => Today.Count == 0;
    public string OverdueText { get; private set; } = "";
    public bool HasOverdue => OverdueText.Length > 0;
    public string Owed { get; private set; } = "";
    public string Overdue { get; private set; } = "";
    public string QuotesOut { get; private set; } = "";

    public void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        services.Store.RaiseAutomaticTasks(services.TaskSettings, today);
        var tasks = services.Store.ListTasks();
        var documents = services.Store.List();

        Today.Clear();
        foreach (var item in Agenda.Build(today, today, services.Store.ListEvents(today, today), tasks, documents).Where(i => !i.Done))
            Today.Add(new AgendaItemViewModel(item));
        var overdueTasks = tasks.Count(t => TaskGroups.Of(t, today) == TaskGroup.Overdue);
        OverdueText = overdueTasks switch { 0 => "", 1 => "1 overdue to-do", _ => $"{overdueTasks} overdue to-dos" };

        // Until payments come from Xero, everything in Xero counts as open.
        var invoices = documents.Where(d => d.Status == DocumentStatus.InXero && d.Document.Kind == DocumentKind.Invoice).ToList();
        Owed = Money.Rand(invoices.Sum(d => d.Document.Total ?? 0));
        Overdue = Money.Rand(invoices.Where(d => d.Document.DueOrExpiryDate < today).Sum(d => d.Document.Total ?? 0));
        QuotesOut = Money.Rand(documents
            .Where(d => d.Status == DocumentStatus.InXero && d.Document.Kind == DocumentKind.Quote && !(d.Document.DueOrExpiryDate < today))
            .Sum(d => d.Document.Total ?? 0));

        OnPropertyChanged(string.Empty);
    }
}
