using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// One line in a menu on the Mail page: the right-click menu on messages and the Categories and Move buttons.
/// The view turns these into menu items, so the menus stay in one place.
/// </summary>
public sealed record MailMenuItem(string Header, Func<Task>? Run = null)
{
    public IReadOnlyList<MailMenuItem> Items { get; init; } = [];
    public bool? Checked { get; init; }
    public string? Colour { get; init; }
    public string? Icon { get; init; }
    public string? Gesture { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsSeparator { get; init; }

    public static MailMenuItem Separator { get; } = new("-") { IsSeparator = true };
}

/// <summary>What you can do to the selected mail: read marks, Important, categories, move, archive and delete.</summary>
public partial class MailViewModel
{
    /// <summary>The picked messages, or the open one when nothing in the list is picked.</summary>
    internal IReadOnlyList<MailSummary> Targets()
    {
        // A picked conversation means all of its mail.
        var picked = Selection.SelectedItems.OfType<MailRowViewModel>().SelectMany(r => r.Thread).ToList();
        return picked.Count > 0 ? picked : Reader is { } reader ? [reader.Summary] : [];
    }

    private IEnumerable<(MailAccount Account, List<MailSummary> Mail)> ByMailbox(IEnumerable<MailSummary> mail) =>
        mail.GroupBy(m => m.AccountId)
            .Select(g => (Account: accounts.FirstOrDefault(a => a.Id == g.Key), Mail: g.ToList()))
            .Where(g => g.Account is not null)
            .Select(g => (g.Account!, g.Mail));

    private static string Count(int n) => n == 1 ? "1 message" : $"{n} messages";

    public bool TargetsUnread => Targets().Any(m => !m.Seen);
    public bool TargetsImportant => Targets().Count > 0 && Targets().All(m => m.Flagged);

    [RelayCommand]
    private Task ToggleRead() => Mark(MailMark.Read, Targets().Any(m => !m.Seen));

    [RelayCommand]
    private Task MarkRead() => Mark(MailMark.Read, true);

    [RelayCommand]
    private Task MarkUnread() => Mark(MailMark.Read, false);

    /// <summary>Marks the selected mail Important, or takes the mark off when all of it already has it.</summary>
    [RelayCommand]
    private Task ToggleImportant() => Mark(MailMark.Important, !Targets().All(m => m.Flagged));

    public Task SetImportant(IReadOnlyList<MailSummary> mail, bool on) => Mark(MailMark.Important, on, mail);

    private async Task Mark(MailMark mark, bool on, IReadOnlyList<MailSummary>? mail = null)
    {
        var changing = (mail ?? Targets()).Where(m => mark == MailMark.Read ? m.Seen != on : m.Flagged != on).ToList();
        if (changing.Count == 0)
            return;
        var updated = new List<MailSummary>();
        foreach (var (account, group) in ByMailbox(changing))
            await Run(async () => updated.AddRange(await services.Mail.MarkAsync(account, group, mark, on)), quiet: true);
        Updated(updated);
        NotifyTargets();
    }

    /// <summary>Puts mail in a category or takes it out. With no mail given, the selected mail.</summary>
    public async Task SetCategory(string keyword, bool on, IReadOnlyList<MailSummary>? mail = null)
    {
        var changing = (mail ?? Targets()).Where(m => MailCategories.Has(m, keyword) != on).ToList();
        if (changing.Count == 0)
            return;
        var updated = new List<MailSummary>();
        foreach (var (account, group) in ByMailbox(changing))
            await Run(async () => updated.AddRange(await services.Mail.SetCategoryAsync(account, group, keyword, on)), quiet: true);
        Updated(updated);
    }

    [RelayCommand]
    private async Task Archive()
    {
        var mail = Targets();
        if (mail.Count == 0)
            return;
        var gone = new List<MailSummary>();
        foreach (var (account, group) in ByMailbox(mail))
            if (await Run(() => services.Mail.ArchiveAsync(account, group), quiet: true))
                gone.AddRange(group);
        if (gone.Count == 0)
            return;
        RemovedAndReload(gone);
        SetStatus($"Archived {Count(gone.Count)}.", false);
    }

    [RelayCommand]
    private async Task Delete()
    {
        var mail = Targets();
        if (mail.Count == 0)
            return;
        var gone = new List<MailSummary>();
        foreach (var (account, group) in ByMailbox(mail))
            if (await Run(() => services.Mail.DeleteAsync(account, group), quiet: true))
                gone.AddRange(group);
        if (gone.Count == 0)
            return;
        RemovedAndReload(gone);
        SetStatus($"Deleted {Count(gone.Count)}.", false);
    }

    /// <summary>
    /// Moves mail to a folder. Mail can only move within its own mailbox, so mail from another mailbox stays put.
    /// With no mail given, the selected mail.
    /// </summary>
    public async Task MoveTo(FolderNodeViewModel folder, IReadOnlyList<MailSummary>? mail = null)
    {
        if (folder.Kind != FolderKind.Folder)
            return;
        var all = mail ?? Targets();
        var moving = all.Where(m => m.AccountId == folder.Mailbox.Id && m.Folder != folder.Path).ToList();
        if (moving.Count == 0)
        {
            if (all.Any(m => m.AccountId != folder.Mailbox.Id))
                SetStatus("Mail can only move to a folder in its own mailbox.", true);
            return;
        }
        if (!await Run(() => services.Mail.MoveAsync(folder.Mailbox, moving, folder.Path), quiet: true))
            return;
        RemovedAndReload(moving);
        var stayed = all.Count(m => m.AccountId != folder.Mailbox.Id);
        SetStatus($"Moved {Count(moving.Count)} to {folder.Name}." + (stayed > 0 ? $" {Count(stayed)} from another mailbox stayed where it was." : ""), false);
    }

    /// <summary>Takes the mail off the list. In views that gather mail from many folders, the list is read again.</summary>
    private void RemovedAndReload(IReadOnlyCollection<MailSummary> gone)
    {
        Removed(gone);
        NotifyTargets();
    }

    private void NotifyTargets()
    {
        OnPropertyChanged(nameof(TargetsUnread));
        OnPropertyChanged(nameof(TargetsImportant));
    }

    /// <summary>The keys on the message list. Returns whether the key was used.</summary>
    public bool HandleKey(string key, bool shift)
    {
        switch (key)
        {
            case "Delete" or "Back":
                DeleteCommand.Execute(null);
                return true;
            case "E":
                ArchiveCommand.Execute(null);
                return true;
            case "I":
                ToggleImportantCommand.Execute(null);
                return true;
            case "U":
                ToggleReadCommand.Execute(null);
                return true;
            case "R" when Reader is { CanWrite: true } reader && SelectedCount <= 1:
                (shift ? reader.ReplyAllCommand : reader.ReplyCommand).Execute(null);
                return true;
            case "F" when Reader is { CanWrite: true } reader && SelectedCount <= 1:
                reader.ForwardCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The keys that work on the message list, for the F1 list.</summary>
    public static IReadOnlyList<(string Keys, string What)> ListKeys { get; } =
    [
        ("Ctrl+click, Shift+click", "Select more than one message"),
        ("Delete", "Delete the selected mail"),
        ("E", "Archive"),
        ("I", "Important on or off"),
        ("U", "Read or unread"),
        ("R, Shift+R", "Reply, reply to all"),
        ("F", "Forward"),
    ];

    /// <summary>The right-click menu for the selected mail.</summary>
    public IReadOnlyList<MailMenuItem> MessageMenu()
    {
        var mail = Targets();
        if (mail.Count == 0)
            return [];
        var items = new List<MailMenuItem>();
        if (mail.Count == 1 && Reader is { CanWrite: true, IsNotDraft: true } reader)
        {
            items.Add(new("Reply", () => Do(reader.ReplyCommand)) { Gesture = "R" });
            items.Add(new("Reply all", () => Do(reader.ReplyAllCommand)) { Gesture = "Shift+R" });
            items.Add(new("Forward", () => Do(reader.ForwardCommand)) { Gesture = "F" });
            items.Add(MailMenuItem.Separator);
        }
        var unread = mail.Any(m => !m.Seen);
        items.Add(new(unread ? "Mark as read" : "Mark as unread", () => Mark(MailMark.Read, unread)) { Icon = "MailOpen", Gesture = "U" });
        var important = mail.All(m => m.Flagged);
        items.Add(new(important ? "Not important" : "Mark important", () => Mark(MailMark.Important, !important)) { Icon = "Flag", Gesture = "I" });
        items.Add(new("Categories") { Icon = "Tag", Items = CategoryMenu() });
        items.Add(new("Move to") { Icon = "FolderMove", Items = MoveMenu() });
        items.Add(new("Snooze") { Icon = "Snooze", Items = SnoozeMenu() });
        items.Add(new("Archive", () => Do(ArchiveCommand)) { Icon = "Archive", Gesture = "E" });
        items.Add(MailMenuItem.Separator);
        items.Add(new("Delete", () => Do(DeleteCommand)) { Icon = "Trash", Gesture = "Delete" });
        return items;
    }

    /// <summary>Every category with a tick when all the selected mail is in it, and a way to make a new one.</summary>
    public IReadOnlyList<MailMenuItem> CategoryMenu()
    {
        var mail = Targets();
        var items = categories.Select(c =>
        {
            var all = mail.Count > 0 && mail.All(m => MailCategories.Has(m, c.Keyword));
            return new MailMenuItem(c.Name, () => SetCategory(c.Keyword, !all)) { Checked = all, Colour = c.Colour, IsEnabled = mail.Count > 0 };
        }).ToList();
        if (items.Count > 0)
            items.Add(MailMenuItem.Separator);
        items.Add(new("New category…", () =>
        {
            StartNewCategory(mail);
            return Task.CompletedTask;
        }));
        return items;
    }

    /// <summary>The folders the selected mail can move to: those of its own mailbox.</summary>
    public IReadOnlyList<MailMenuItem> MoveMenu()
    {
        var mail = Targets();
        var mailboxes = mail.Select(m => m.AccountId).Distinct().ToList();
        if (mailboxes.Count != 1)
            return [new("Select mail from one mailbox to move it") { IsEnabled = false }];
        var here = mail.Select(m => m.Folder).Distinct().ToList();
        return Mailboxes.SelectMany(m => m.Folders)
            .Where(f => f.Kind == FolderKind.Folder && f.Mailbox.Id == mailboxes[0] && f.Role != FolderRole.Drafts
                && !(here.Count == 1 && here[0] == f.Path))
            .Select(f => new MailMenuItem(new string(' ', f.Depth * 4) + f.Name, () => MoveTo(f, mail)) { Icon = f.Icon })
            .ToList();
    }

    private static Task Do(System.Windows.Input.ICommand command)
    {
        if (command is IAsyncRelayCommand run)
            return run.ExecuteAsync(null);
        command.Execute(null);
        return Task.CompletedTask;
    }
}
