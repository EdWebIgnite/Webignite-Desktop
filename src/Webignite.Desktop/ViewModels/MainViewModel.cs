using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel()
    {
        var settings = new DocumentSettings();
        Modules =
        [
            new Module("Overview", new OverviewViewModel()),
            new Module("New quote", new DocumentEditorViewModel(DocumentKind.Quote, settings)),
            new Module("New invoice", new DocumentEditorViewModel(DocumentKind.Invoice, settings)),
            new Module("To approve", new ComingLaterViewModel("To approve",
                "Quotes and invoices that Claude or ChatGPT prepare will wait here for your approval before anything goes to Xero.")),
            new Module("Mail", new ComingLaterViewModel("Mail",
                "Your mailbox, built in. Read and reply to client mail next to their quotes and invoices."), IsLater: true),
            new Module("Settings", new ComingLaterViewModel("Settings",
                "Connect Xero, set due days and connect Claude Desktop and ChatGPT.")),
        ];
        Selected = Modules[0];
    }

    public IReadOnlyList<Module> Modules { get; }

    [ObservableProperty]
    public partial Module Selected { get; set; }

    public string OrganisationName => "Webignite";
}
