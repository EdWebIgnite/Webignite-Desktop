using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

/// <summary>Keyboard shortcuts for the whole window. F1 lists them, and so does Settings, Shortcuts.</summary>
public partial class MainViewModel
{
    // What each shortcut that works from any program does, as the list says it.
    private static readonly Dictionary<GlobalShortcut, string> FromAnyProgram = new()
    {
        [GlobalShortcut.Screenshot] = "Take a screenshot",
        [GlobalShortcut.Recording] = "Start or stop recording the screen",
        [GlobalShortcut.QuickNote] = "A quick note, to-do or event",
        [GlobalShortcut.ColourPicker] = "Pick a colour off the screen and copy its code",
        [GlobalShortcut.ClipboardHistory] = "Clipboard history, the last things you copied",
        [GlobalShortcut.Ruler] = "Measure something on the screen",
        [GlobalShortcut.Snippets] = "Paste a text snippet where you are typing",
        [GlobalShortcut.ScreenText] = "Copy words off the screen",
    };

    /// <summary>
    /// Every keyboard shortcut, under the part of the app it belongs to. The ones that work from any program are
    /// listed with the keys picked in Settings, and left out when turned off.
    /// </summary>
    public static IReadOnlyList<Shortcut> AllShortcuts(ScreenshotSettings shots, string quickNote) =>
    [
        new("Ctrl+K", "Search clients, people, quotes, invoices, mail, to-dos, notes, site logins and files (while writing, it adds a link)", ShortcutList.Everywhere),
        new("Ctrl+N", "New on this page: quote, invoice, client, retainer, domain, mail or note", ShortcutList.Everywhere),
        new("Ctrl+M", "Open or close the menu of every page, tool and project", ShortcutList.Everywhere),
        new("Ctrl+1 to Ctrl+9", "Jump to a page, in sidebar order", ShortcutList.Everywhere),
        new("Ctrl+B", "Fold the sidebar to icons, tuck it away, or bring it back (while writing, it is bold)", ShortcutList.Everywhere),
        new("Ctrl+Shift+1 to 4", "Show or hide the first, second, third or fourth pane (Mail, Clients, Notes, Files)", ShortcutList.Everywhere),
        new("F5", "Read this page again, or check for mail on Mail", ShortcutList.Everywhere),
        new("F1", "Show or hide this list", ShortcutList.Everywhere),
        new("Esc", "Close this list or the search box, or go back from what's open", ShortcutList.Everywhere),
        .. GlobalShortcuts.All
            .Select(info => (Info: info, Keys: GlobalShortcuts.Keys(info.Shortcut, shots, quickNote)))
            .Where(g => !string.IsNullOrWhiteSpace(g.Keys))
            .Select(g => new Shortcut(g.Keys, $"{FromAnyProgram[g.Info.Shortcut]} (change it in Settings, {g.Info.Place})", ShortcutList.AnyProgram)),
        new("Ctrl+T", "New tab", ShortcutList.Tabs),
        new("Ctrl+W", "Close this tab (in a popped-out window: close the window)", ShortcutList.Tabs),
        new("Ctrl+Tab", "Next tab (with Shift, the one before)", ShortcutList.Tabs),
        new("Ctrl+click", "In the sidebar: open the page in a new tab (or click with the middle button)", ShortcutList.Tabs),
        new("Middle click", "On a tab: close it", ShortcutList.Tabs),
        new("Down, Up", "Move between rows", ShortcutList.Lists),
        new("Enter", "Open the row", ShortcutList.Lists),
        new("Esc", "Close the open client, quote or invoice", ShortcutList.Lists),
        .. MailViewModel.ListKeys.Select(k => new Shortcut(k.Keys, "In the list: " + k.What, ShortcutList.Mail)),
        new("Ctrl+A", "In the list: select every mail", ShortcutList.Mail),
        new("Ctrl+B, Ctrl+I, Ctrl+U", "Bold, italic, underline", ShortcutList.Writing),
        new("Ctrl+Space", "Remove formatting", ShortcutList.Writing),
        new("Ctrl+Shift+8", "Bullets on or off", ShortcutList.Writing),
        new("Ctrl+Shift+7", "Numbered list on or off", ShortcutList.Writing),
        new("Ctrl+Shift+9", "Checklist on or off", ShortcutList.Writing),
        new("Ctrl+] / Ctrl+[", "Bigger or smaller text", ShortcutList.Writing),
        new("Ctrl+K", "Add a link", ShortcutList.Writing),
        new("Ctrl+Z, Ctrl+Y", "Undo, redo", ShortcutList.Writing),
        new("Ctrl+Shift+V", "Paste as plain text", ShortcutList.Writing),
        new("Tab, Shift+Tab", "Indent a list item, or the next and previous table cell", ShortcutList.Writing),
        new("Shift+Enter", "A new line in the same paragraph", ShortcutList.Writing),
        new("Ctrl+F", "Search all notes", ShortcutList.Notes),
        new("Ctrl+Shift+C", "Checklist on or off", ShortcutList.Notes),
        new("Ctrl+click", "Open a link", ShortcutList.Notes),
        new("Ctrl+Enter, Esc", "Quick note: save it (only Discard throws it away)", ShortcutList.Notes),
        new("F2, Delete", "Rename (several at once when more are picked), or send to the Recycle Bin", ShortcutList.Files),
        new("Ctrl+C, Ctrl+X, Ctrl+V", "Copy, cut and paste, to and from Explorer too", ShortcutList.Files),
        new("Ctrl+Shift+C", "Copy as path", ShortcutList.Files),
        new("Alt+Enter", "Properties", ShortcutList.Files),
        new("Ctrl+Z", "Put back the last rename, move, copy or new folder", ShortcutList.Files),
        new("Ctrl+Shift+N", "New folder", ShortcutList.Files),
        new("Alt+Left, Alt+Right, Alt+Up", "Back, forward, up a folder (Backspace goes back too)", ShortcutList.Files),
        new("Ctrl+L", "Type a path", ShortcutList.Files),
        new("Ctrl+F", "Search this folder and everything below it", ShortcutList.Files),
        new("Space", "Quick Look, a big preview; arrows go through the folder", ShortcutList.Files),
        new("Left, Right", "Column view: back a column, or open the picked folder", ShortcutList.Files),
        new("Ctrl+double-click", "Open a folder in a new tab", ShortcutList.Files),
        new("Enter, Esc", "Open and Save box: pick or save, cancel", ShortcutList.Files),
        new("Enter, Esc, Backspace", "Calculator: work it out, clear, rub out; the number pad works too", ShortcutList.Tools),
        new("Ctrl+V, Ctrl+Z", "Mark-up: paste a screenshot, undo the last mark", ShortcutList.Tools),
        new("Delete", "PDF tools: take out the picked pages", ShortcutList.Tools),
        new("Enter, Esc", "Taking a screenshot: the whole screen, stop", ShortcutList.Tools),
    ];

    /// <summary>The heading a page's own shortcuts are under, so they are listed first while it is open; null when it has none.</summary>
    public static string? ShortcutGroupOf(Page page) => page switch
    {
        Page.Mail => ShortcutList.Mail,
        Page.Files => ShortcutList.Files,
        Page.Notes => ShortcutList.Notes,
        Page.Clients or Page.Quotes or Page.Invoices => ShortcutList.Lists,
        Page.Markup or Page.Pdf => ShortcutList.Tools,
        _ => null,
    };

    /// <summary>The list F1 shows. Filled when it opens and emptied when it closes, so it holds nothing meanwhile.</summary>
    public ShortcutsPart ShortcutsPart => field ??= new ShortcutsPart(
        () => AllShortcuts(services.Settings.Screenshots, services.Settings.Notes.QuickShortcut), () => ShortcutGroupOf(Current.Key));

    [ObservableProperty] public partial bool ShowingShortcuts { get; set; }

    partial void OnShowingShortcutsChanged(bool value)
    {
        if (value)
            ShortcutsPart.Open();
        else
            ShortcutsPart.Close();
    }

    [RelayCommand]
    private void ToggleShortcuts() => ShowingShortcuts = !ShowingShortcuts;

    [RelayCommand]
    private void HideShortcuts() => ShowingShortcuts = false;

    /// <summary>F1's "Change shortcuts": Settings, where the list is too and the ones from any program are picked.</summary>
    [RelayCommand]
    private void ShortcutSettings()
    {
        ShowingShortcuts = false;
        services.Go.Settings("shortcuts");
    }

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
