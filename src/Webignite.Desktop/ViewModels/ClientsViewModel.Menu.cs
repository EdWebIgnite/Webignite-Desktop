using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

// The right-click menu on a client in the list: open it here, in a tab or a window, mail them, start a quote
// or invoice for them, see their notes or folder, copy their address or number.
public partial class ClientsViewModel
{
    /// <summary>Puts text on the clipboard; the view hands this in.</summary>
    public Func<string, Task>? CopyText { get; set; }

    [RelayCommand]
    private void OpenInNewTab(ClientRow row) => services.Go.ClientInNewTab(row.Client);

    [RelayCommand]
    private void OpenInNewWindow(ClientRow row) => services.Go.ClientInNewWindow(row.Client);

    [RelayCommand]
    private void WriteTo(ClientRow row)
    {
        if (row.Client.Email is { Length: > 0 } email)
            services.Go.WriteMail(Recipients.Parse(email).People);
    }

    [RelayCommand]
    private void NewQuoteFor(ClientRow row)
    {
        Open(row);
        Detail?.NewQuoteCommand.Execute(null);
    }

    [RelayCommand]
    private void NewInvoiceFor(ClientRow row)
    {
        Open(row);
        Detail?.NewInvoiceCommand.Execute(null);
    }

    [RelayCommand]
    private void NotesFor(ClientRow row) => services.Go.ClientNotes(row.Client.Id, row.Client.Name);

    [RelayCommand]
    private void FolderFor(ClientRow row)
    {
        var root = Files.FileOps.ClientRoot(services.Settings.Files);
        var match = Webignite.Core.Files.ClientFolders.For(row.Client, root is null ? [] : Files.FileOps.Folders(root));
        if (match.Folder is { } folder && Directory.Exists(folder))
            services.Go.Folder(folder);
        else
            Open(row);
    }

    [RelayCommand]
    private Task CopyEmail(ClientRow row) => row.Client.Email is { Length: > 0 } email && CopyText is { } copy ? copy(email) : Task.CompletedTask;

    [RelayCommand]
    private Task CopyPhone(ClientRow row) => row.Client.Phone is { Length: > 0 } phone && CopyText is { } copy ? copy(phone) : Task.CompletedTask;
}
