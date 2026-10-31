using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly AppServices services;

    public MainViewModel(AppServices services)
    {
        this.services = services;
        services.CatchUp();
        Quotes = new DocumentsViewModel(DocumentKind.Quote, services);
        Invoices = new DocumentsViewModel(DocumentKind.Invoice, services);
        Todo = new TodoViewModel(services);
        Overview = new OverviewViewModel(services);
        Clients = new ClientsViewModel(services);
        Mail = new MailViewModel(services);
        Modules =
        [
            new Module("Overview", Overview),
            new Module("Clients", Clients),
            new Module("Quotes", Quotes),
            new Module("Invoices", Invoices),
            new Module("Retainers", new RetainersViewModel(services)),
            new Module("Domains and hosting", new RenewalsViewModel(services)),
            new Module("Money", new MoneyViewModel(services)),
            new Module("Reports", new ReportsViewModel(services)),
            new Module("To-do", Todo),
            new Module("Calendar", new CalendarViewModel(services)),
            new Module("Planner", new PlannerViewModel(services)),
            new Module("To approve", new ComingLaterViewModel("To approve",
                "Quotes and invoices that Claude or ChatGPT prepare will wait here for your approval before anything goes to Xero.")),
            new Module("Mail", Mail, FullBleed: true),
            new Module("Settings", new SettingsViewModel(services)),
        ];
        Selected = Modules[0];
        var mailModule = Modules.Single(m => m.Page == Mail);
        services.OpenMail = summary =>
        {
            Selected = mailModule;
            Mail.OpenMessage(summary);
        };
        services.WriteMail = to =>
        {
            Selected = mailModule;
            Mail.WriteTo(to);
        };
    }

    public DocumentsViewModel Quotes { get; }
    public DocumentsViewModel Invoices { get; }
    public TodoViewModel Todo { get; }
    public OverviewViewModel Overview { get; }
    public ClientsViewModel Clients { get; }
    public MailViewModel Mail { get; }
    public IReadOnlyList<Module> Modules { get; }

    [ObservableProperty]
    public partial Module Selected { get; set; }

    // These pages show things other pages change, so they look again whenever they're opened.
    partial void OnSelectedChanged(Module value)
    {
        services.CatchUp();
        switch (value.Page)
        {
            case DocumentsViewModel documents: documents.Refresh(); break;
            case RetainersViewModel retainers: retainers.Refresh(); break;
            case RenewalsViewModel renewals: renewals.Refresh(); break;
            case TodoViewModel todo: todo.Refresh(); break;
            case CalendarViewModel calendar: calendar.Refresh(); break;
            case PlannerViewModel planner: planner.Refresh(); break;
            case OverviewViewModel overview: overview.Refresh(); break;
            case ClientsViewModel clients: clients.Refresh(); break;
            case MoneyViewModel money: money.Refresh(); break;
            case ReportsViewModel reports: reports.Refresh(); break;
            case MailViewModel mail: mail.Reload(); _ = mail.SyncAll(); break;
            case SettingsViewModel settings: settings.UpdateAssistantStatus(); break;
        }
    }
}
