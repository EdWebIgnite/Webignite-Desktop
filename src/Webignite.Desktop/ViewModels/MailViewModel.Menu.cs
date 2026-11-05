using Webignite.Core.Clients;
using Webignite.Core.Mail;
using Webignite.Core.Tasks;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

// More on the right-click menu of a mail: open it in a tab or window of its own, write to the sender, copy their
// address, open or add their client, make a to-do of it, save it as a file, select everything.
public partial class MailViewModel
{
    /// <summary>Puts text on the clipboard; the view hands this in, as the clipboard belongs to the window.</summary>
    public Func<string, Task>? CopyText { get; set; }

    /// <summary>Asks where to save a file (the app's own Save box) and returns the path, or null.</summary>
    public Func<string, Task<string?>>? AskSavePath { get; set; }

    /// <summary>Selects every row in the list; the view hands this in.</summary>
    public Action? SelectAll { get; set; }

    private IEnumerable<MailMenuItem> MoreForOne(MailSummary mail)
    {
        yield return new("Open in a new tab", () =>
        {
            services.Go.MailInNewTab(mail);
            return Task.CompletedTask;
        }) { Icon = "Tiles" };
        yield return new("Open in a new window", () =>
        {
            services.Go.MailInNewWindow(mail);
            return Task.CompletedTask;
        }) { Icon = "NewWindow" };
        if (mail.From is { Address.Length: > 0 } from)
        {
            yield return MailMenuItem.Separator;
            var who = string.IsNullOrWhiteSpace(from.Name) ? from.Address : from.Name!;
            yield return new($"Write to {who}", () =>
            {
                services.Go.WriteMail([new MailPerson(from.Name, from.Address)]);
                return Task.CompletedTask;
            }) { Icon = "Send" };
            yield return new("Copy their address", () => CopyText?.Invoke(from.Address) ?? Task.CompletedTask);
            var client = ClientMail.For(services.Clients.List(), from.Address);
            yield return client is not null
                ? new($"Open {client.Name}", () =>
                {
                    services.Go.Client(client);
                    return Task.CompletedTask;
                }) { Icon = "Clients" }
                : new($"Add {who} as a client", () =>
                {
                    AddClientFrom(from, who);
                    return Task.CompletedTask;
                }) { Icon = "Clients" };
        }
        yield return MailMenuItem.Separator;
        yield return new("Make a to-do of it", () =>
        {
            MakeTodo(mail);
            return Task.CompletedTask;
        }) { Icon = "Todo" };
        yield return new("Save as a file…", () => SaveAsFile(mail)) { Icon = "Download" };
    }

    private void AddClientFrom(MailPerson from, string who)
    {
        var client = services.Clients.Add(who);
        services.Clients.Change(client.Id, c => c with { Email = from.Address, ContactPerson = string.IsNullOrWhiteSpace(from.Name) ? null : from.Name });
        if (services.Clients.Get(client.Id) is { } saved)
            services.Go.Client(saved);
    }

    // A to-do to deal with the mail, naming who it's from and their client when known.
    private void MakeTodo(MailSummary mail)
    {
        var client = ClientMail.For(services.Clients.List(), mail.From?.Address);
        var subject = string.IsNullOrWhiteSpace(mail.Subject) ? "(no subject)" : mail.Subject;
        services.Store.SaveTask(new TaskItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = $"Deal with mail: {subject}",
            Notes = $"From {mail.From?.Display ?? "someone"}, {mail.Date.ToLocalTime():d MMM yyyy HH:mm}.",
            Customer = client?.Name,
            Created = DateTimeOffset.UtcNow,
        });
        services.ShowNote?.Invoke("Added to your to-do list", subject, () => services.Go.To(Page.Todo));
    }

    // The whole mail as an .eml file, which Outlook and other mail programs open.
    private async Task SaveAsFile(MailSummary mail)
    {
        if (AskSavePath is null || accounts.FirstOrDefault(a => a.Id == mail.AccountId) is not { } account)
            return;
        var name = Webignite.Core.Files.FileNames.Safe(string.IsNullOrWhiteSpace(mail.Subject) ? "Mail" : mail.Subject) + ".eml";
        if (await AskSavePath(name) is not { } path)
            return;
        await Run(async () =>
        {
            var message = await services.Mail.GetMessageAsync(account, mail.Folder, mail.Uid);
            await File.WriteAllBytesAsync(path, message);
        });
        Status = $"Saved {Path.GetFileName(path)}.";
    }

    /// <summary>Marks every mail in a folder read, from its right-click menu.</summary>
    public async Task MarkFolderRead(FolderNodeViewModel folder)
    {
        if (!folder.IsRealFolder)
            return;
        var unread = services.Store.ListMailSummaries(folder.Mailbox.Id, folder.Path, 5000).Where(m => !m.Seen).ToList();
        if (unread.Count == 0)
            return;
        var updated = new List<MailSummary>();
        await Run(async () => updated.AddRange(await services.Mail.MarkAsync(folder.Mailbox, unread, MailMark.Read, true)), quiet: true);
        Updated(updated);
        Reload();
    }
}
