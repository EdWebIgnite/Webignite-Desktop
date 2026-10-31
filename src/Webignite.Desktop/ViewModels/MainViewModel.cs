using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel(AppServices services)
    {
        Quotes = new DocumentsViewModel(DocumentKind.Quote, services);
        Invoices = new DocumentsViewModel(DocumentKind.Invoice, services);
        Todo = new TodoViewModel(services);
        Overview = new OverviewViewModel(services);
        Clients = new ClientsViewModel(services);
        Modules =
        [
            new Module("Overview", Overview),
            new Module("Clients", Clients),
            new Module("Quotes", Quotes),
            new Module("Invoices", Invoices),
            new Module("To-do", Todo),
            new Module("Calendar", new CalendarViewModel(services)),
            new Module("Planner", new PlannerViewModel(services)),
            new Module("To approve", new ComingLaterViewModel("To approve",
                "Quotes and invoices that Claude or ChatGPT prepare will wait here for your approval before anything goes to Xero.")),
            new Module("Mail", new ComingLaterViewModel("Mail",
                "Your mailbox, built in. Read and reply to client mail next to their quotes and invoices."), IsLater: true),
            new Module("Settings", new SettingsViewModel(services)),
        ];
        Selected = Modules[0];
    }

    public DocumentsViewModel Quotes { get; }
    public DocumentsViewModel Invoices { get; }
    public TodoViewModel Todo { get; }
    public OverviewViewModel Overview { get; }
    public ClientsViewModel Clients { get; }
    public IReadOnlyList<Module> Modules { get; }

    [ObservableProperty]
    public partial Module Selected { get; set; }

    // These pages show things other pages change, so they look again whenever they're opened.
    partial void OnSelectedChanged(Module value)
    {
        switch (value.Page)
        {
            case TodoViewModel todo: todo.Refresh(); break;
            case CalendarViewModel calendar: calendar.Refresh(); break;
            case PlannerViewModel planner: planner.Refresh(); break;
            case OverviewViewModel overview: overview.Refresh(); break;
            case ClientsViewModel clients: clients.Refresh(); break;
        }
    }
}
