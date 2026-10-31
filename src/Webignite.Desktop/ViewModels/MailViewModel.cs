using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The Mail module: mailboxes and folders on the left, messages in the middle, the open message on
/// the right. Everything shown comes from the copy on this PC; the servers are checked in the background.
/// </summary>
public partial class MailViewModel : ViewModelBase
{
    private readonly AppServices services;
    private IReadOnlyList<MailAccount> accounts = [];
    private bool syncing;

    public MailViewModel(AppServices services)
    {
        this.services = services;
        Setup = new MailSetupViewModel(services, OnSetupDone);
        Reload();
    }

    public ObservableCollection<MailboxNodeViewModel> Mailboxes { get; } = [];
    public ObservableCollection<MailRowViewModel> Messages { get; } = [];
    public MailSetupViewModel Setup { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListTitle))]
    public partial FolderNodeViewModel? CurrentFolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReader))]
    public partial MailReaderViewModel? Reader { get; set; }

    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool StatusIsProblem { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMail))]
    public partial bool ShowSetup { get; set; }

    public bool ShowMail => !ShowSetup;
    public bool HasReader => Reader is not null;
    public bool NoMessages => Messages.Count == 0;
    public string EmptyText => Search.Trim().Length > 0 ? "Nothing matches that search." : "No mail in this folder.";

    public string ListTitle => Search.Trim().Length > 0 ? "Search results"
        : CurrentFolder is { } f ? (f.IsAllInboxes ? "All inboxes" : $"{f.Name}  ·  {f.Mailbox.Address}")
        : "";

    internal IReadOnlyList<MailAccount> Accounts => accounts;

    partial void OnSearchChanged(string value)
    {
        OnPropertyChanged(nameof(ListTitle));
        OnPropertyChanged(nameof(EmptyText));
        ShowMessages();
    }

    /// <summary>Re-reads mailboxes and folders from this PC, keeping the open folder if it still exists.</summary>
    public void Reload()
    {
        accounts = services.Store.ListMailAccounts();
        var current = CurrentFolder;
        Mailboxes.Clear();
        if (accounts.Count > 1)
            Mailboxes.Add(MailboxNodeViewModel.AllInboxes(this, accounts.Sum(a => services.Store.ListMailFolders(a.Id).FirstOrDefault(f => f.Role == FolderRole.Inbox)?.Unread ?? 0)));
        foreach (var account in accounts)
            Mailboxes.Add(new MailboxNodeViewModel(account, services.Store.ListMailFolders(account.Id), this));

        var folders = Mailboxes.SelectMany(m => m.Folders).ToList();
        CurrentFolder = folders.FirstOrDefault(f => current is not null && f.Key == current.Key) ?? folders.FirstOrDefault();
        foreach (var folder in folders)
            folder.IsCurrent = folder == CurrentFolder;
        ShowSetup = accounts.Count == 0 || ShowSetup && Setup.IsOpen;
        if (accounts.Count == 0)
            Setup.StartNew();
        ShowMessages();
    }

    public void Open(FolderNodeViewModel folder)
    {
        CurrentFolder = folder;
        foreach (var f in Mailboxes.SelectMany(m => m.Folders))
            f.IsCurrent = f == folder;
        Search = "";
        Reader = null;
        ShowMessages();
        if (!folder.IsAllInboxes)
            _ = Run(() => services.Mail.SyncFolderAsync(folder.Mailbox, folder.Path));
    }

    private void ShowMessages()
    {
        var selected = Reader?.Summary;
        Messages.Clear();
        IEnumerable<MailSummary> list;
        if (Search.Trim().Length > 0)
            list = services.Store.SearchMail(accounts.Select(a => a.Id), Search.Trim());
        else if (CurrentFolder is { IsAllInboxes: true })
            list = accounts
                .SelectMany(a => services.Store.ListMailFolders(a.Id).Where(f => f.Role == FolderRole.Inbox)
                    .SelectMany(f => services.Store.ListMailSummaries(a.Id, f.Path)))
                .OrderByDescending(s => s.Date).Take(500);
        else if (CurrentFolder is { } folder)
            list = services.Store.ListMailSummaries(folder.Mailbox.Id, folder.Path);
        else
            list = [];

        var showMailbox = accounts.Count > 1 && (Search.Trim().Length > 0 || CurrentFolder?.IsAllInboxes == true);
        foreach (var summary in list)
        {
            var account = accounts.FirstOrDefault(a => a.Id == summary.AccountId);
            if (account is null)
                continue;
            var row = new MailRowViewModel(summary, account, showMailbox, this);
            row.IsSelected = selected is not null && selected.AccountId == summary.AccountId && selected.Folder == summary.Folder && selected.Uid == summary.Uid;
            Messages.Add(row);
        }
        OnPropertyChanged(nameof(NoMessages));
        OnPropertyChanged(nameof(EmptyText));
    }

    public async Task Select(MailRowViewModel row)
    {
        foreach (var r in Messages)
            r.IsSelected = r == row;
        var reader = new MailReaderViewModel(row.Summary, row.Account, services, this);
        Reader = reader;
        await reader.Load();
        if (!row.Summary.Seen)
        {
            await Run(() => services.Mail.SetSeenAsync(row.Account, row.Summary, true), quiet: true);
            row.MarkSeen();
            RefreshCounts();
        }
    }

    [RelayCommand]
    private Task Refresh() => SyncAll();

    /// <summary>Checks every mailbox for new mail. Safe to call often; a check already running is not doubled.</summary>
    public async Task SyncAll()
    {
        if (syncing || accounts.Count == 0)
            return;
        syncing = true;
        try
        {
            var failed = new List<string>();
            foreach (var account in accounts)
            {
                if (!services.Mail.HasPassword(account))
                {
                    failed.Add($"{account.Address}: password not saved yet");
                    continue;
                }
                var ok = await Run(() => services.Mail.SyncAccountAsync(account), quiet: true, onError: e => failed.Add($"{account.Address}: {e}"));
                if (!ok)
                    continue;
            }
            if (CurrentFolder is { IsAllInboxes: false } folder && folder.Role is not (FolderRole.Inbox or FolderRole.Sent or FolderRole.Drafts))
                await Run(() => services.Mail.SyncFolderAsync(folder.Mailbox, folder.Path), quiet: true, onError: e => failed.Add(e));
            Reload();
            SetStatus(failed.Count == 0 ? $"Up to date at {DateTime.Now:HH:mm}" : string.Join("  ·  ", failed), failed.Count > 0);
        }
        finally
        {
            syncing = false;
        }
    }

    [RelayCommand]
    private Task LoadOlder() => CurrentFolder is { IsAllInboxes: false } folder
        ? Run(() => services.Mail.SyncFolderAsync(folder.Mailbox, folder.Path, older: true))
        : Task.CompletedTask;

    [RelayCommand]
    private void ManageMailboxes()
    {
        Setup.StartNew();
        ShowSetup = true;
    }

    public void EditMailbox(MailAccount account)
    {
        Setup.Edit(account);
        ShowSetup = true;
    }

    private void OnSetupDone(bool changed)
    {
        ShowSetup = false;
        Reload();
        if (changed)
            _ = SyncAll();
    }

    /// <summary>Called by the reader after it moves or deletes a message.</summary>
    internal void Removed(MailSummary summary)
    {
        Reader = null;
        var row = Messages.FirstOrDefault(r => r.Summary.AccountId == summary.AccountId && r.Summary.Folder == summary.Folder && r.Summary.Uid == summary.Uid);
        if (row is not null)
            Messages.Remove(row);
        OnPropertyChanged(nameof(NoMessages));
        RefreshCounts();
    }

    internal void Changed(MailSummary updated)
    {
        var index = Messages.ToList().FindIndex(r => r.Summary.AccountId == updated.AccountId && r.Summary.Folder == updated.Folder && r.Summary.Uid == updated.Uid);
        if (index >= 0)
            Messages[index] = new MailRowViewModel(updated, Messages[index].Account, Messages[index].ShowMailbox, this) { IsSelected = Messages[index].IsSelected };
        RefreshCounts();
    }

    private void RefreshCounts()
    {
        foreach (var mailbox in Mailboxes)
            foreach (var folder in mailbox.Folders.Where(f => !f.IsAllInboxes))
                folder.Unread = services.Store.ListMailSummaries(folder.Mailbox.Id, folder.Path).Count(s => !s.Seen);
        if (Mailboxes.FirstOrDefault()?.Folders.FirstOrDefault() is { IsAllInboxes: true } all)
            all.Unread = Mailboxes.SelectMany(m => m.Folders).Where(f => !f.IsAllInboxes && f.Role == FolderRole.Inbox).Sum(f => f.Unread);
    }

    internal void SetStatus(string text, bool problem)
    {
        Status = text;
        StatusIsProblem = problem;
    }

    /// <summary>Runs a server job with a busy marker, turning failures into a plain message. Returns whether it worked.</summary>
    internal async Task<bool> Run(Func<Task> job, bool quiet = false, Action<string>? onError = null)
    {
        IsBusy = true;
        try
        {
            await job();
            if (!quiet)
            {
                Reload();
                SetStatus($"Up to date at {DateTime.Now:HH:mm}", false);
            }
            return true;
        }
        catch (MailException e)
        {
            if (onError is not null)
                onError(e.Message);
            else
                SetStatus(e.Message, true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public partial class MailboxNodeViewModel : ViewModelBase
{
    private MailboxNodeViewModel(string title, string address, IReadOnlyList<FolderNodeViewModel> folders, MailAccount? account, MailViewModel owner)
    {
        Title = title;
        Address = address;
        Folders = folders;
        Account = account;
        this.owner = owner;
    }

    private readonly MailViewModel owner;

    public MailboxNodeViewModel(MailAccount account, IReadOnlyList<MailFolder> folders, MailViewModel owner)
        : this(account.DisplayName, account.Address, [], account, owner)
    {
        Folders = (folders.Count == 0
                ? [new MailFolder { AccountId = account.Id, Path = "INBOX", Name = "Inbox", Role = FolderRole.Inbox }]
                : folders)
            .Select(f => new FolderNodeViewModel(f, account, owner))
            .ToList();
    }

    public static MailboxNodeViewModel AllInboxes(MailViewModel owner, int unread)
    {
        var node = new MailboxNodeViewModel("", "", [], null, owner);
        node.Folders = [FolderNodeViewModel.AllInboxes(owner, unread)];
        return node;
    }

    public string Title { get; }
    public string Address { get; }
    public MailAccount? Account { get; }
    public bool HasTitle => Account is not null;
    public IReadOnlyList<FolderNodeViewModel> Folders { get; private set; }

    [RelayCommand]
    private void Edit()
    {
        if (Account is not null)
            owner.EditMailbox(Account);
    }
}

public partial class FolderNodeViewModel : ViewModelBase
{
    private readonly MailViewModel owner;

    public FolderNodeViewModel(MailFolder folder, MailAccount mailbox, MailViewModel owner)
    {
        this.owner = owner;
        Mailbox = mailbox;
        Path = folder.Path;
        Name = folder.Name;
        Role = folder.Role;
        Depth = Role == FolderRole.Other ? Math.Max(0, folder.Path.Count(c => c is '.' or '/') - (folder.Path.StartsWith("INBOX", StringComparison.OrdinalIgnoreCase) ? 1 : 0)) : 0;
        Unread = folder.Unread;
    }

    private FolderNodeViewModel(MailViewModel owner, int unread)
    {
        this.owner = owner;
        Mailbox = null!;
        Path = "";
        Name = "All inboxes";
        Role = FolderRole.Inbox;
        IsAllInboxes = true;
        Unread = unread;
    }

    public static FolderNodeViewModel AllInboxes(MailViewModel owner, int unread) => new(owner, unread);

    public MailAccount Mailbox { get; }
    public string Path { get; }
    public string Name { get; }
    public FolderRole Role { get; }
    public bool IsAllInboxes { get; }
    public int Depth { get; }
    public Avalonia.Thickness Indent => new(10 + Depth * 14, 6, 10, 6);
    public string Key => IsAllInboxes ? "*" : Mailbox.Id + "|" + Path;

    /// <summary>Unread counts only show where they help: inboxes and your own folders, not Sent or Trash.</summary>
    public string UnreadText => Unread > 0 && Role is not (FolderRole.Sent or FolderRole.Trash or FolderRole.Drafts or FolderRole.Junk) ? Unread.ToString() : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadText))]
    public partial int Unread { get; set; }

    [ObservableProperty] public partial bool IsCurrent { get; set; }

    [RelayCommand]
    private void Open() => owner.Open(this);
}

public partial class MailRowViewModel(MailSummary summary, MailAccount account, bool showMailbox, MailViewModel owner) : ViewModelBase
{
    public MailSummary Summary { get; private set; } = summary;
    public MailAccount Account => account;
    public bool ShowMailbox => showMailbox;

    private bool IsOutgoing => Summary.Folder.EndsWith("Sent", StringComparison.OrdinalIgnoreCase)
        || Summary.Folder.EndsWith("Drafts", StringComparison.OrdinalIgnoreCase);

    public string Who => IsOutgoing
        ? "To " + (Summary.To.Count == 0 ? "nobody yet" : string.Join(", ", Summary.To.Select(p => p.Display)))
        : Summary.From?.Display ?? "Unknown sender";

    public string Subject => string.IsNullOrWhiteSpace(Summary.Subject) ? "(no subject)" : Summary.Subject;
    public string Preview => Summary.Preview;
    public bool IsUnread => !Summary.Seen;
    public bool IsFlagged => Summary.Flagged;
    public bool HasAttachments => Summary.HasAttachments;
    public string MailboxTag => account.Address.Split('@')[0];

    public string When
    {
        get
        {
            var local = Summary.Date.ToLocalTime();
            var today = DateTime.Today;
            return local.Date == today ? local.ToString("HH:mm")
                : local.Date > today.AddDays(-7) ? local.ToString("ddd HH:mm")
                : local.Year == today.Year ? local.ToString("d MMM")
                : local.ToString("d MMM yyyy");
        }
    }

    [ObservableProperty] public partial bool IsSelected { get; set; }

    internal void MarkSeen()
    {
        Summary = Summary with { Seen = true };
        OnPropertyChanged(nameof(IsUnread));
    }

    [RelayCommand]
    private Task Open() => owner.Select(this);
}
