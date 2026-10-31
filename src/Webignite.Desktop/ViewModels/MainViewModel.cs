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
        Modules =
        [
            new Module("Overview", new OverviewViewModel()),
            new Module("Quotes", Quotes),
            new Module("Invoices", Invoices),
            new Module("To-do", Todo),
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
    public IReadOnlyList<Module> Modules { get; }

    [ObservableProperty]
    public partial Module Selected { get; set; }

    // Reminders depend on documents, so look again whenever the to-do list is opened.
    partial void OnSelectedChanged(Module value)
    {
        if (value.Page is TodoViewModel todo)
            todo.Refresh();
    }
}
