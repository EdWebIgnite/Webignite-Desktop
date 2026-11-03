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
        new("F5", "Read this page again, or check for mail on Mail"),
        new("F1", "Show or hide this list"),
        new("Esc", "Close this list or the search box"),
    ];

    [ObservableProperty] public partial bool ShowingShortcuts { get; set; }

    [RelayCommand]
    private void ToggleShortcuts() => ShowingShortcuts = !ShowingShortcuts;

    [RelayCommand]
    private void HideShortcuts() => ShowingShortcuts = false;

    /// <summary>Ctrl+1 to Ctrl+9: the pages in sidebar order.</summary>
    [RelayCommand]
    private void GoTo(string number)
    {
        if (int.TryParse(number, out var n) && n >= 1 && n <= Modules.Count)
            Selected = Modules[n - 1];
    }

    /// <summary>Ctrl+N: starts something new on pages that make things.</summary>
    [RelayCommand]
    private void New()
    {
        switch (Selected.Page)
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
        if (Selected.Page == Mail)
            await CheckMailAsync();
        else if (Selected.Page is IRefreshable page)
            page.Refresh();
    }
}
