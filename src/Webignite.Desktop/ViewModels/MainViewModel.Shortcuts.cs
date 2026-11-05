using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Webignite.Desktop.ViewModels;

/// <summary>Keyboard shortcuts for the whole window. F1 lists them.</summary>
public partial class MainViewModel
{
    public sealed record Shortcut(string Keys, string What);

    public static IReadOnlyList<Shortcut> Shortcuts { get; } =
    [
        new("Ctrl+Alt+S", "From any program: a screenshot (change it in Settings, Screenshots)"),
        new("Ctrl+Alt+R", "From any program: start or stop recording the screen"),
        new("Ctrl+Alt+N", "From any program: a quick note, to-do or event (change it in Settings, Notes)"),
        new("Ctrl+Alt+C", "From any program: pick a colour off the screen and copy its code (Settings, Screenshots)"),
        new("Ctrl+K", "Search clients, quotes, invoices, mail, to-dos, notes, site logins and files"),
        new("Ctrl+N", "New on this page: quote, invoice, client, retainer, domain, mail or note"),
        new("Ctrl+1 to Ctrl+9", "Jump to a page, in sidebar order"),
        new("Ctrl+B", "Fold the sidebar to icons, tuck it away, or bring it back"),
        new("Ctrl+Shift+1 to 4", "Show or hide the first, second, third or fourth pane (Mail, Clients, Notes, Files)"),
        new("F5", "Read this page again, or check for mail on Mail"),
        new("F1", "Show or hide this list"),
        new("Esc", "Close this list or the search box, or go back from what's open"),
        new("Ctrl+T", "New tab"),
        new("Ctrl+W", "Close this tab (in a popped-out window: close the window)"),
        new("Ctrl+Tab", "Next tab (with Shift, the one before)"),
        new("Ctrl+click", "In the sidebar: open the page in a new tab (or click with the middle button)"),
        new("Middle click", "On a tab: close it"),
        new("Down, Up", "Clients, Quotes and Invoices: move between rows"),
        new("Enter", "Clients, Quotes and Invoices: open the row"),
        new("Esc", "Close the open client, quote or invoice"),
        .. MailViewModel.ListKeys.Select(k => new Shortcut(k.Keys, "Mail list: " + k.What)),
        new("Ctrl+A", "Mail list: select every mail"),
        new("Ctrl+B, Ctrl+I, Ctrl+U", "Writing: bold, italic, underline"),
        new("Ctrl+Space", "Writing: remove formatting"),
        new("Ctrl+] / Ctrl+[", "Writing: bigger or smaller text"),
        new("Ctrl+K", "Writing: add a link"),
        new("Ctrl+Z, Ctrl+Y", "Writing: undo, redo"),
        new("Ctrl+Shift+V", "Writing: paste as plain text"),
        new("Tab, Shift+Tab", "Writing: indent a list item, or the next and previous table cell"),
        new("Shift+Enter", "Writing: a new line in the same paragraph"),
        new("Ctrl+F", "Notes: search all notes"),
        new("Ctrl+Shift+C", "Notes: checklist on or off"),
        new("Ctrl+click", "Notes: open a link"),
        new("Ctrl+Enter, Esc", "Quick note: save it (only Discard throws it away)"),
        new("F2, Delete", "Files: rename (several at once when more are picked), or send to the Recycle Bin"),
        new("Ctrl+C, Ctrl+X, Ctrl+V", "Files: copy, cut and paste, to and from Explorer too"),
        new("Ctrl+Shift+C", "Files: copy as path"),
        new("Alt+Enter", "Files: properties"),
        new("Ctrl+Z", "Files: put back the last rename, move, copy or new folder"),
        new("Ctrl+Shift+N", "Files: new folder"),
        new("Alt+Left, Alt+Right, Alt+Up", "Files: back, forward, up a folder (Backspace goes back too)"),
        new("Ctrl+L", "Files: type a path"),
        new("Ctrl+F", "Files: search this folder and everything below it"),
        new("Space", "Files: Quick Look, a big preview; arrows go through the folder"),
        new("Left, Right", "Files, column view: back a column, or open the picked folder"),
        new("Ctrl+double-click", "Files: open a folder in a new tab"),
        new("Enter, Esc", "Open and Save box: pick or save, cancel"),
        new("Enter, Esc, Backspace", "Calculator: work it out, clear, rub out; the number pad works too"),
        new("Ctrl+V, Ctrl+Z", "Mark-up: paste a screenshot, undo the last mark"),
        new("Delete", "PDF tools: take out the picked pages"),
        new("Enter, Esc", "Taking a screenshot: the whole screen, stop"),
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

    /// <summary>Ctrl+Shift+1 to 4: shows or hides that pane on a page with panes (Mail, Clients, Notes, Files).</summary>
    [RelayCommand]
    private void TogglePane(string number)
    {
        if (int.TryParse(number, out var n) && Current.Page is IHasPanes page)
            page.Panels.Toggle(n - 1);
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
            case NotesViewModel notes: notes.NewNoteCommand.Execute(null); break;
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
