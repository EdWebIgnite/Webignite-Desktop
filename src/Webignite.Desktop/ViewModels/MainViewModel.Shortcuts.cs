using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Webignite.Desktop.ViewModels;

/// <summary>Keyboard shortcuts for the whole window. F1 lists them.</summary>
public partial class MainViewModel
{
    public sealed record Shortcut(string Keys, string What);

    public static IReadOnlyList<Shortcut> Shortcuts { get; } =
    [
        new("Ctrl+K", "Search clients, quotes, invoices and mail"),
        new("Ctrl+N", "New on this page: quote, invoice, client, retainer, domain or mail"),
        new("Ctrl+1 to Ctrl+9", "Jump to a page, in sidebar order"),
        new("Ctrl+B", "Fold the sidebar to icons, tuck it away, or bring it back"),
        new("F5", "Read this page again, or check for mail on Mail"),
        new("F1", "Show or hide this list"),
        new("Esc", "Close this list or the search box"),
        new("Down, Up", "Clients, Quotes and Invoices: move between rows"),
        new("Enter", "Clients, Quotes and Invoices: open the row"),
        new("Esc", "Close the open client, quote or invoice"),
        .. MailViewModel.ListKeys.Select(k => new Shortcut(k.Keys, "Mail list: " + k.What)),
    ];

    [ObservableProperty] public partial bool ShowingShortcuts { get; set; }

    [RelayCommand]
    private void ToggleShortcuts() => ShowingShortcuts = !ShowingShortcuts;

    [RelayCommand]
    private void HideShortcuts() => ShowingShortcuts = false;

    /// <summary>
    /// Esc: goes back from what is open on the page, the way its Back or Close does (saving what was typed).
    /// A quote or invoice opened from a client goes back to the client. Returns false when nothing is open.
    /// </summary>
    public bool GoBack()
    {
        switch (Current.Page)
        {
            case DocumentsViewModel { Editor: { } editor }:
                editor.BackCommand.Execute(null);
                return true;
            case ClientsViewModel { Detail: { Editor: { } editor } }:
                editor.BackCommand.Execute(null);
                return true;
            case ClientsViewModel { Detail: { } client }:
                client.BackCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Ctrl+1 to Ctrl+9: the pages in sidebar order.</summary>
    [RelayCommand]
    private void GoTo(string number)
    {
        if (int.TryParse(number, out var n) && n >= 1 && n <= Sidebar.Pages.Count)
            Selected = Sidebar.Pages[n - 1];
    }

    /// <summary>Ctrl+N: starts something new on pages that make things.</summary>
    [RelayCommand]
    private void New()
    {
        switch (Current.Page)
        {
            // Never over the top of something already open, so nothing typed is lost.
            case DocumentsViewModel { Editor: null } documents: documents.NewCommand.Execute(null); break;
            case ClientsViewModel { Detail: null } clients: clients.NewCommand.Execute(null); break;
            case RetainersViewModel { Editor: null } retainers: retainers.NewCommand.Execute(null); break;
            case RenewalsViewModel { ShowForm: false } renewals: renewals.NewCommand.Execute(null); break;
            case MailViewModel { Composer: null } mail: mail.WriteCommand.Execute(null); break;
        }
    }

    /// <summary>F5: reads the page again. On Mail it checks for new mail.</summary>
    [RelayCommand]
    private async Task RefreshPage()
    {
        if (Current.Key == Page.Mail)
            await CheckMailAsync();
        else if (Current.Page is IRefreshable page)
            page.Refresh();
    }
}
