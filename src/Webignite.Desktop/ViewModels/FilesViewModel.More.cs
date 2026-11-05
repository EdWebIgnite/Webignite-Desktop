using Webignite.Core.Clients;
using Webignite.Core.Files;

namespace Webignite.Desktop.ViewModels;

// More on Files' right-click: a folder in a window of its own, files in a new mail, a picture in Mark-up, files
// linked to a client's page, and the folder itself in Windows Terminal.
public partial class FilesViewModel
{
    public void OpenInNewWindow(FileRowViewModel row) => services.Go.FolderInNewWindow(row.Entry.Path);

    /// <summary>The picked files attached to a new mail, from the everyday mailbox.</summary>
    public async Task MailPicked()
    {
        var files = Selected.Where(r => !r.IsFolder).Select(r => r.Entry.Path).ToList();
        if (files.Count == 0)
            return;
        if (services.Drafts.EverydayMailbox is not { } mailbox)
        {
            Say("Add a mailbox in Mail first.", problem: true);
            return;
        }
        var attachments = new List<Webignite.Core.Mail.OutgoingAttachment>();
        foreach (var path in files)
            attachments.Add(await services.Drafts.AttachAsync(Path.GetFileName(path), await File.ReadAllBytesAsync(path)));
        var subject = files.Count == 1 ? Path.GetFileNameWithoutExtension(files[0]) : $"{files.Count} files";
        services.Drafts.Open(new MailDraft(mailbox, null, subject, "") { Attachments = attachments });
    }

    public bool CanMarkup => Selected is [{ IsFolder: false } one] && QuickActions.CanRotate(one.Name);

    public void MarkupPicked()
    {
        if (Selected is [{ IsFolder: false } one])
            services.Go.Markup(one.Entry.Path);
    }

    /// <summary>The clients, A to Z, for linking the picked files to one.</summary>
    public IReadOnlyList<Client> ClientsToLink() => services.Clients.List();

    public void LinkPickedTo(Client client)
    {
        var paths = SelectedPaths;
        if (paths.Count == 0)
            return;
        services.Clients.Change(client.Id, c => ClientRecords.AddFiles(c, paths, DateOnly.FromDateTime(DateTime.Today)));
        Say(paths.Count == 1 ? $"Linked to {client.Name}'s page." : $"{paths.Count} files linked to {client.Name}'s page.", problem: false);
    }

    /// <summary>Windows Terminal (or PowerShell) opened in this folder.</summary>
    public void OpenTerminal()
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(Folder))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wt.exe", $"-d \"{Folder}\"") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe") { WorkingDirectory = Folder, UseShellExecute = true });
        }
    }

    public bool FolderIsFavourite => IsFavourite(Folder);
}
