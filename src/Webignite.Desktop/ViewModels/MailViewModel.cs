using System.Collections.ObjectModel;
using Avalonia.Controls.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The Mail module: mailboxes, folders and categories on the left, messages in the middle, the open message on
/// the right. Everything shown comes from the copy on this PC; the servers are checked in the background.
/// What you can do to the selected mail (mark, move, archive, categories) is in MailViewModel.Actions.cs, and
/// making folders and categories in MailViewModel.Editors.cs.
/// </summary>
public partial class MailViewModel : ViewModelBase
{
    private readonly AppServices services;
    private IReadOnlyList<MailAccount> accounts = [];
    private IReadOnlyList<MailCategory> categories = [];
    private bool syncing;
    private bool rebuilding;

    public MailViewModel(AppServices services)
    {
        this.services = services;
        Setup = new MailSetupViewModel(services, OnSetupDone);
        Panels = new PanelsViewModel(services.Settings, "mail",
            [("folders", "Folders"), ("messages", "Messages"), ("reader", "Reading pane")], spine: "messages");
        Selection = new SelectionModel<MailRowViewModel> { SingleSelect = false, Source = Messages };
        Selection.SelectionChanged += (_, _) => OnSelectionChanged();
        Filters = Enum.GetValues<MailFilter>().Select(f => new FilterChipViewModel(f, this)).ToList();
        Reload();
    }

    /// <summary>Folders, messages and the reading pane: which show, in what order, how wide.</summary>
    public PanelsViewModel Panels { get; }

    public ObservableCollection<MailboxNodeViewModel> Mailboxes { get; } = [];
    public ObservableCollection<CategoryNodeViewModel> Categories { get; } = [];
    public ObservableCollection<MailRowViewModel> Messages { get; } = [];

    /// <summary>Which messages are selected. A click selects one, Ctrl and Shift select more.</summary>
    public SelectionModel<MailRowViewModel> Selection { get; }

    public IReadOnlyList<FilterChipViewModel> Filters { get; }
    public MailSetupViewModel Setup { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListTitle), nameof(CanLoadOlder))]
    public partial FolderNodeViewModel? CurrentFolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListTitle), nameof(CanLoadOlder))]
    public partial CategoryNodeViewModel? CurrentCategory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReader), nameof(ShowReader), nameof(ShowNothing), nameof(HasTargets), nameof(ShowActions))]
    public partial MailReaderViewModel? Reader { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComposer), nameof(ShowReader), nameof(ShowNothing), nameof(ShowMany), nameof(ShowActions))]
    public partial ComposeViewModel? Composer { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMany), nameof(ShowNothing), nameof(ManyText), nameof(HasTargets), nameof(ShowActions))]
    public partial int SelectedCount { get; set; }

    [ObservableProperty] public partial MailFilter Filter { get; set; } = MailFilter.All;

    public bool HasComposer => Composer is not null;
    public bool ShowReader => Reader is not null && Composer is null && SelectedCount <= 1;
    public bool ShowMany => SelectedCount > 1 && Composer is null;
    public bool ShowNothing => Reader is null && Composer is null && SelectedCount <= 1;
    public string ManyText => $"{SelectedCount} messages selected";

    /// <summary>Whether the buttons for marking, moving and deleting have something to work on.</summary>
    public bool HasTargets => SelectedCount > 0 || Reader is not null;
    public bool ShowActions => HasTargets && Composer is null;

    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool StatusIsProblem { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMail))]
    public partial bool ShowSetup { get; set; }

    public bool ShowMail => !ShowSetup && !ShowRules;
    public bool HasReader => Reader is not null;
    public bool NoMessages => Messages.Count == 0;
    public bool HasCategories => Categories.Count > 0;

    public string EmptyText => Search.Trim().Length > 0 ? "Nothing matches that search."
        : CurrentCategory is not null && Filter == MailFilter.All ? "No mail in this category yet. Right-click a message to add it, or drag it here."
        : CurrentFolder is { Kind: FolderKind.Important } && Filter == MailFilter.All ? "Nothing is marked important."
        : MailFilters.Empty(Filter);

    public string ListTitle => Search.Trim().Length > 0 ? "Search results"
        : CurrentCategory is { } category ? category.Name
        : CurrentFolder is { } f ? f.Kind switch
        {
            FolderKind.AllInboxes => "All inboxes",
            FolderKind.Important => "Important",
            _ => $"{f.Name}  ·  {f.Mailbox.Address}",
        }
        : "";

    /// <summary>The key the open list is known by, for settings kept per list.</summary>
    private string? ListKey => Search.Trim().Length > 0 ? null : CurrentCategory?.Key ?? CurrentFolder?.Key;

    /// <summary>Replies grouped under the first mail, one row per conversation. Each list can switch it off; search never groups.</summary>
    public bool Conversations => ListKey is { } key && services.Settings.MailView.ConversationsIn(key);
    public bool CanGroup => ListKey is not null;

    [RelayCommand]
    private void ToggleConversations()
    {
        if (ListKey is not { } key)
            return;
        services.Settings.MailView = services.Settings.MailView.WithConversations(key, !Conversations);
        OnPropertyChanged(nameof(Conversations));
        ShowMessages();
    }

    /// <summary>Older mail can only be fetched for a real folder, not for Important or a category.</summary>
    public bool CanLoadOlder => CurrentFolder is { Kind: FolderKind.Folder } && CurrentCategory is null;

    internal IReadOnlyList<MailAccount> Accounts => accounts;
    internal IReadOnlyList<MailCategory> CategoryList => categories;
    internal AppServices Services => services;

    partial void OnSearchChanged(string value)
    {
        OnPropertyChanged(nameof(ListTitle));
        OnPropertyChanged(nameof(EmptyText));
        ShowMessages();
    }

    partial void OnFilterChanged(MailFilter value)
    {
        foreach (var chip in Filters)
            chip.IsChosen = chip.Filter == value;
        OnPropertyChanged(nameof(EmptyText));
        ShowMessages();
    }

    /// <summary>Re-reads mailboxes, folders and categories from this PC, keeping the open folder if it still exists.</summary>
    public void Reload()
    {
        accounts = services.Store.ListMailAccounts();
        var currentKey = CurrentCategory?.Key ?? CurrentFolder?.Key;
        Mailboxes.Clear();
        Mailboxes.Add(MailboxNodeViewModel.Smart(this, accounts.Count > 1
            ? accounts.Sum(a => services.Store.ListMailFolders(a.Id).FirstOrDefault(f => f.Role == FolderRole.Inbox)?.Unread ?? 0)
            : (int?)null));
        foreach (var account in accounts)
            Mailboxes.Add(new MailboxNodeViewModel(account, services.Store.ListMailFolders(account.Id), this));
        LoadCategories();

        var folders = Mailboxes.SelectMany(m => m.Folders).ToList();
        CurrentCategory = Categories.FirstOrDefault(c => c.Key == currentKey);
        CurrentFolder = CurrentCategory is not null ? null
            : folders.FirstOrDefault(f => f.Key == currentKey)
              ?? folders.FirstOrDefault(f => f.Kind == FolderKind.AllInboxes)
              ?? folders.FirstOrDefault(f => f.Kind == FolderKind.Folder);
        MarkCurrent();
        ShowSetup = accounts.Count == 0 || ShowSetup && Setup.IsOpen;
        if (accounts.Count == 0)
            Setup.StartNew();
        ShowMessages();
    }

    private void LoadCategories()
    {
        categories = services.Settings.MailCategories;
        Categories.Clear();
        foreach (var category in categories)
            Categories.Add(new CategoryNodeViewModel(category, this));
        OnPropertyChanged(nameof(HasCategories));
    }

    private void MarkCurrent()
    {
        foreach (var f in Mailboxes.SelectMany(m => m.Folders))
            f.IsCurrent = CurrentCategory is null && f == CurrentFolder;
        foreach (var c in Categories)
            c.IsCurrent = c == CurrentCategory;
    }

    public void Open(FolderNodeViewModel folder)
    {
        CurrentCategory = null;
        CurrentFolder = folder;
        OpenView();
        if (folder.Kind == FolderKind.Folder)
            Run(() => services.Mail.SyncFolderAsync(folder.Mailbox, folder.Path)).Forget("Checking a mail folder");
    }

    /// <summary>Shows every mail in a category, from all mailboxes and folders except Trash and Junk.</summary>
    public void Open(CategoryNodeViewModel category)
    {
        CurrentCategory = category;
        CurrentFolder = null;
        OpenView();
    }

    private void OpenView()
    {
        MarkCurrent();
        rebuilding = true;
        Selection.Clear();
        rebuilding = false;
        SelectedCount = 0;
        Search = "";
        Reader = null;
        ShowMessages();
        OnPropertyChanged(nameof(EmptyText));
    }

    private void ShowMessages()
    {
        var picked = Selection.SelectedItems.OfType<MailRowViewModel>().Select(r => Key(r.Summary)).ToHashSet();
        if (picked.Count == 0 && Reader is { } reader)
            picked.Add(Key(reader.Summary));

        bins = null;
        IEnumerable<MailSummary> list;
        var ids = accounts.Select(a => a.Id).ToList();
        if (Search.Trim().Length > 0)
            list = services.Store.SearchMail(ids, Search.Trim());
        else if (CurrentCategory is { } category)
            list = services.Store.FindMail(ids, s => MailCategories.Has(s, category.Keyword) && !InBin(s), 500);
        else if (CurrentFolder is { Kind: FolderKind.Important })
            list = services.Store.FindMail(ids, s => s.Flagged && !InBin(s), 500);
        else if (CurrentFolder is { Kind: FolderKind.AllInboxes })
            list = accounts
                .SelectMany(a => services.Store.ListMailFolders(a.Id).Where(f => f.Role == FolderRole.Inbox)
                    .SelectMany(f => services.Store.ListMailSummaries(a.Id, f.Path)))
                .OrderByDescending(s => s.Date).Take(500);
        else if (CurrentFolder is { } folder)
            list = services.Store.ListMailSummaries(folder.Mailbox.Id, folder.Path);
        else
            list = [];

        var showMailbox = accounts.Count > 1 && (Search.Trim().Length > 0 || CurrentCategory is not null || CurrentFolder is { Kind: not FolderKind.Folder });
        var kept = list.Where(s => MailFilters.Keep(s, Filter));
        var arrival = Arrival();
        var threads = Conversations ? MailThreads.Group(kept, arrival) : MailThreads.Single(kept, arrival);
        rebuilding = true;
        try
        {
            Messages.Clear();
            foreach (var thread in threads)
            {
                if (accounts.FirstOrDefault(a => a.Id == thread.Latest.AccountId) is not { } account)
                    continue;
                Messages.Add(new MailRowViewModel(thread.Latest, account, showMailbox, this) { Thread = thread.Messages });
            }
            for (var i = 0; i < Messages.Count; i++)
                if (Messages[i].Thread.Any(m => picked.Contains(Key(m))))
                    Selection.Select(i);
        }
        finally
        {
            rebuilding = false;
        }
        SelectedCount = Selection.SelectedItems.Count;
        OnPropertyChanged(nameof(NoMessages));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(Conversations));
        OnPropertyChanged(nameof(CanGroup));
    }

    private HashSet<(string, string)>? bins;

    /// <summary>Whether a mail sits in Trash or Junk, which Important and the categories leave out.</summary>
    private bool InBin(MailSummary mail)
    {
        bins ??= accounts.SelectMany(a => services.Store.ListMailFolders(a.Id)
                .Where(f => f.Role is FolderRole.Trash or FolderRole.Junk).Select(f => (a.Id, f.Path)))
            .ToHashSet();
        return bins.Contains((mail.AccountId, mail.Folder));
    }

    internal static (string, string, uint) Key(MailSummary mail) => (mail.AccountId, mail.Folder, mail.Uid);

    private void OnSelectionChanged()
    {
        if (rebuilding)
            return;
        var rows = Selection.SelectedItems.OfType<MailRowViewModel>().ToList();
        SelectedCount = rows.Count;
        if (rows.Count == 1)
        {
            if (Reader is null || Key(Reader.Summary) != Key(rows[0].Summary))
                Show(rows[0]).Forget("Opening a mail");
        }
        else if (rows.Count > 1)
        {
            Reader = null;
        }
    }

    /// <summary>Picks one message in the list and opens it.</summary>
    public async Task Select(MailRowViewModel row)
    {
        var index = Messages.IndexOf(row);
        rebuilding = true;
        try
        {
            Selection.Clear();
            if (index >= 0)
                Selection.Select(index);
        }
        finally
        {
            rebuilding = false;
        }
        SelectedCount = Selection.SelectedItems.Count;
        await Show(row);
    }

    private Task Show(MailRowViewModel row) => ShowInThread(row.Summary, row.Account, row.Thread);

    /// <summary>
    /// Opens one mail in the reader, with the rest of its conversation stacked above it. Opening a conversation
    /// marks all of it read.
    /// </summary>
    internal async Task ShowInThread(MailSummary summary, MailAccount account, IReadOnlyList<MailSummary> thread)
    {
        var reader = new MailReaderViewModel(summary, account, services, this) { Thread = thread };
        Reader = reader;
        await reader.Load();
        var unread = thread.Where(m => !m.Seen).ToList();
        if (unread.Count > 0 && Reader == reader)
        {
            var seen = new List<MailSummary>();
            await Run(async () => seen.AddRange(await services.Mail.MarkAsync(account, unread, MailMark.Read, true)), quiet: true);
            Updated(seen);
        }
    }

    [RelayCommand]
    private Task Refresh() => SyncAll();

    /// <summary>A new mail from the mailbox you're looking at, or the first one.</summary>
    [RelayCommand]
    private void Write()
    {
        if (accounts.Count == 0)
            return;
        var from = CurrentFolder is { Kind: FolderKind.Folder } folder ? folder.Mailbox : accounts[0];
        Compose(new OutgoingMail { AccountId = from.Id, Body = Replies.StartBody(from.Signature) });
    }

    /// <summary>A new mail to these people, from Eddie's own mailbox rather than the accounts one.</summary>
    public void WriteTo(IReadOnlyList<MailPerson> to)
    {
        if (accounts.Count == 0)
            return;
        var from = accounts.FirstOrDefault(a => !a.SendsInvoices) ?? accounts[0];
        Compose(new OutgoingMail { AccountId = from.Id, To = to, Body = Replies.StartBody(from.Signature) });
    }

    /// <summary>Shows one mail, opening its folder first. Used from client pages.</summary>
    public void OpenMessage(MailSummary summary)
    {
        if (Mailboxes.SelectMany(m => m.Folders).FirstOrDefault(f => f.Kind == FolderKind.Folder && f.Mailbox.Id == summary.AccountId && f.Path == summary.Folder) is { } folder)
            Open(folder);
        var row = Messages.FirstOrDefault(r => Key(r.Summary) == Key(summary));
        if (row is null && accounts.FirstOrDefault(a => a.Id == summary.AccountId) is { } account)
            row = new MailRowViewModel(summary, account, false, this);
        if (row is not null)
            Select(row).Forget("Opening a mail");
    }

    public bool Compose(OutgoingMail mail, byte[]? source = null, MailSummary? answering = null, Func<Task>? afterSent = null)
    {
        if (Composer is { HasChanges: true } open)
        {
            open.Message = "Finish or close this mail before starting another.";
            open.MessageIsProblem = true;
            return false;
        }
        Reload();
        Composer = new ComposeViewModel(services, this, accounts, mail, source, answering, afterSent);
        return true;
    }

    internal void CloseComposer(ComposeViewModel composer)
    {
        if (Composer == composer)
            Composer = null;
    }

    internal void Sent(ComposeViewModel composer, string note)
    {
        CloseComposer(composer);
        SetStatus(note, note.StartsWith("Sent, but", StringComparison.Ordinal));
        SyncAll().Forget("Checking mail");
    }

    internal void DraftSaved() => ShowMessages();

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
                await Run(() => services.Mail.SyncAccountAsync(account), quiet: true, onError: e => failed.Add($"{account.Address}: {e}"));
            }
            await SendScheduled();
            await WakeSnoozed();
            await RunRulesOnArrival();
            if (CurrentFolder is { Kind: FolderKind.Folder } folder && folder.Role is not (FolderRole.Inbox or FolderRole.Sent or FolderRole.Drafts))
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
    private Task LoadOlder() => CurrentFolder is { Kind: FolderKind.Folder } folder && CurrentCategory is null
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
            SyncAll().Forget("Checking mail");
    }

    /// <summary>Takes mail that was moved or deleted off the list, and opens the next one, as Apple Mail does.</summary>
    internal void Removed(IReadOnlyCollection<MailSummary> gone)
    {
        var keys = gone.Select(Key).ToHashSet();
        // A conversation goes when all its mail has gone; otherwise it keeps what is left.
        foreach (var row in Messages.Where(r => r.Thread.Count > 1 && r.Thread.Any(m => keys.Contains(Key(m)))).ToList())
        {
            var left = row.Thread.Where(m => !keys.Contains(Key(m))).ToList();
            if (left.Count > 0)
                row.SetThread(left);
        }
        var rows = Messages.Where(r => r.Thread.All(m => keys.Contains(Key(m)))).ToList();
        var next = rows.Count == 0 ? -1 : Messages.IndexOf(rows[0]);
        rebuilding = true;
        try
        {
            foreach (var row in rows)
                Messages.Remove(row);
        }
        finally
        {
            rebuilding = false;
        }
        if (Reader is { } reader && keys.Contains(Key(reader.Summary)))
            Reader = null;
        OnPropertyChanged(nameof(NoMessages));
        RefreshCounts(gone);
        if (next >= 0 && Messages.Count > 0 && Selection.SelectedItems.Count == 0)
            Select(Messages[Math.Min(next, Messages.Count - 1)]).Forget("Opening the next mail");
        else
            SelectedCount = Selection.SelectedItems.Count;
    }

    /// <summary>Shows new marks and categories on mail that changed, without rebuilding the list.</summary>
    internal void Updated(IReadOnlyCollection<MailSummary> changed)
    {
        if (changed.Count == 0)
            return;
        var byKey = changed.ToDictionary(Key);
        foreach (var row in Messages)
            if (row.Thread.Any(m => byKey.ContainsKey(Key(m))))
                row.Update(byKey);
        if (Reader is { } reader && byKey.TryGetValue(Key(reader.Summary), out var open))
            reader.Update(open);
        RefreshCounts(changed);
        // In a filtered list or the Important view, mail that no longer fits stays until the list is next shown,
        // so it doesn't jump away under the mouse.
    }

    // Only the folders that changed: counting every folder meant reading thousands of messages per click.
    private void RefreshCounts(IEnumerable<MailSummary> changed)
    {
        var touched = changed.Select(m => (m.AccountId, m.Folder)).ToHashSet();
        foreach (var folder in Mailboxes.SelectMany(m => m.Folders)
                     .Where(f => f.Kind == FolderKind.Folder && touched.Contains((f.Mailbox.Id, f.Path))))
            folder.Unread = services.Store.ListMailSummaries(folder.Mailbox.Id, folder.Path).Count(s => !s.Seen);
        if (Mailboxes.SelectMany(m => m.Folders).FirstOrDefault(f => f.Kind == FolderKind.AllInboxes) is { } all)
            all.Unread = Mailboxes.SelectMany(m => m.Folders).Where(f => f.Kind == FolderKind.Folder && f.Role == FolderRole.Inbox).Sum(f => f.Unread);
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

public partial class FilterChipViewModel(MailFilter filter, MailViewModel owner) : ViewModelBase
{
    public MailFilter Filter => filter;
    public string Label => MailFilters.Label(filter);

    [ObservableProperty] public partial bool IsChosen { get; set; } = filter == MailFilter.All;

    [RelayCommand]
    private void Pick() => owner.Filter = filter;
}

/// <summary>A heading in the folder list: one mailbox and its folders, or the top group with All inboxes and Important.</summary>
public partial class MailboxNodeViewModel : ViewModelBase
{
    private readonly MailViewModel owner;

    private MailboxNodeViewModel(string title, string address, MailAccount? account, MailViewModel owner)
    {
        Title = title;
        Address = address;
        Account = account;
        this.owner = owner;
    }

    public MailboxNodeViewModel(MailAccount account, IReadOnlyList<MailFolder> folders, MailViewModel owner)
        : this(account.DisplayName, account.Address, account, owner)
    {
        Folders = (folders.Count == 0
                ? [new MailFolder { AccountId = account.Id, Path = "INBOX", Name = "Inbox", Role = FolderRole.Inbox }]
                : folders)
            .Where(f => !Snooze.IsSnoozeFolder(f)) // the Snoozed folder stays out of sight
            .Select(f => new FolderNodeViewModel(f, account, owner))
            .ToList();
    }

    /// <summary>All inboxes (when there is more than one mailbox) and Important.</summary>
    public static MailboxNodeViewModel Smart(MailViewModel owner, int? allInboxesUnread)
    {
        var node = new MailboxNodeViewModel("", "", null, owner);
        node.Folders = allInboxesUnread is { } unread
            ? [FolderNodeViewModel.SmartView(FolderKind.AllInboxes, owner, unread), FolderNodeViewModel.SmartView(FolderKind.Important, owner, 0)]
            : [FolderNodeViewModel.SmartView(FolderKind.Important, owner, 0)];
        return node;
    }

    public string Title { get; }
    public string Address { get; }
    public MailAccount? Account { get; }
    public bool HasTitle => Account is not null;
    public IReadOnlyList<FolderNodeViewModel> Folders { get; private set; } = [];

    [RelayCommand]
    private void Edit()
    {
        if (Account is not null)
            owner.EditMailbox(Account);
    }

    /// <summary>Starts a new folder inside this mailbox's Inbox.</summary>
    [RelayCommand]
    private void NewFolder()
    {
        if (Account is not null)
            owner.StartNewFolder(Account, null);
    }
}

public enum FolderKind
{
    Folder,
    AllInboxes,
    Important,
}

public partial class FolderNodeViewModel : ViewModelBase
{
    private readonly MailViewModel owner;

    public FolderNodeViewModel(MailFolder folder, MailAccount mailbox, MailViewModel owner)
    {
        this.owner = owner;
        Mailbox = mailbox;
        Folder = folder;
        Path = folder.Path;
        Name = folder.Name;
        Role = folder.Role;
        Depth = Role == FolderRole.Other ? Math.Max(0, folder.Path.Count(c => c is '.' or '/') - (folder.Path.StartsWith("INBOX", StringComparison.OrdinalIgnoreCase) ? 1 : 0)) : 0;
        Unread = folder.Unread;
    }

    private FolderNodeViewModel(FolderKind kind, MailViewModel owner, int unread)
    {
        this.owner = owner;
        Mailbox = null!;
        Folder = null!;
        Kind = kind;
        Path = "";
        Name = kind == FolderKind.AllInboxes ? "All inboxes" : "Important";
        Role = kind == FolderKind.AllInboxes ? FolderRole.Inbox : FolderRole.Other;
        Unread = unread;
    }

    public static FolderNodeViewModel SmartView(FolderKind kind, MailViewModel owner, int unread) => new(kind, owner, unread);

    public FolderKind Kind { get; }
    public MailAccount Mailbox { get; }
    public MailFolder Folder { get; }
    public string Path { get; }
    public string Name { get; }
    public FolderRole Role { get; }
    public bool IsAllInboxes => Kind == FolderKind.AllInboxes;
    public bool IsImportant => Kind == FolderKind.Important;
    public bool IsRealFolder => Kind == FolderKind.Folder;
    public int Depth { get; }
    public Avalonia.Thickness Indent => new(10 + Depth * 14, 6, 10, 6);

    public string Key => Kind switch
    {
        FolderKind.AllInboxes => "*",
        FolderKind.Important => "!important",
        _ => Mailbox.Id + "|" + Path,
    };

    /// <summary>The icon name from Styles/Icons.axaml for this folder's job.</summary>
    public string Icon => Kind switch
    {
        FolderKind.AllInboxes => "Inbox",
        FolderKind.Important => "Flag",
        _ => Role switch
        {
            FolderRole.Inbox => "Inbox",
            FolderRole.Drafts => "Draft",
            FolderRole.Sent => "Send",
            FolderRole.Archive => "Archive",
            FolderRole.Junk => "Junk",
            FolderRole.Trash => "Trash",
            _ => "Folder",
        },
    };

    /// <summary>Only folders you made can be renamed or removed.</summary>
    public bool CanChange => Kind == FolderKind.Folder && FolderNames.CanChange(Folder);

    /// <summary>Unread counts only show where they help: inboxes and your own folders, not Sent or Trash.</summary>
    public string UnreadText => Unread > 0 && Role is not (FolderRole.Sent or FolderRole.Trash or FolderRole.Drafts or FolderRole.Junk) ? Unread.ToString() : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadText))]
    public partial int Unread { get; set; }

    [ObservableProperty] public partial bool IsCurrent { get; set; }

    /// <summary>Lit up while mail is dragged over it.</summary>
    [ObservableProperty] public partial bool IsDropTarget { get; set; }

    [RelayCommand]
    private void Open() => owner.Open(this);

    [RelayCommand]
    private void NewFolder()
    {
        if (Kind == FolderKind.Folder)
            owner.StartNewFolder(Mailbox, this);
    }

    [RelayCommand]
    private void Rename()
    {
        if (CanChange)
            owner.StartRenameFolder(this);
    }

    [RelayCommand]
    private void Remove()
    {
        if (CanChange)
            owner.StartRemoveFolder(this);
    }
}

/// <summary>One of your categories in the folder list: its dot, its name, and the mail in it when clicked.</summary>
public partial class CategoryNodeViewModel(MailCategory category, MailViewModel owner) : ViewModelBase
{
    public MailCategory Category => category;
    public string Keyword => category.Keyword;
    public string Name => category.Name;
    public string Colour => category.Colour;
    public string Key => "#" + category.Keyword;

    [ObservableProperty] public partial bool IsCurrent { get; set; }
    [ObservableProperty] public partial bool IsDropTarget { get; set; }

    [RelayCommand]
    private void Open() => owner.Open(this);

    [RelayCommand]
    private void Edit() => owner.StartEditCategory(category);
}

public partial class MailRowViewModel(MailSummary summary, MailAccount account, bool showMailbox, MailViewModel owner) : ViewModelBase
{
    /// <summary>The mail the row shows: the newest in its conversation.</summary>
    public MailSummary Summary { get; private set; } = summary;

    /// <summary>All the mail in this row's conversation, oldest first. Just the one mail when conversations are off.</summary>
    public IReadOnlyList<MailSummary> Thread { get; internal set; } = [summary];

    /// <summary>How many mails the conversation holds, shown beside the sender when more than one.</summary>
    public int Count => Thread.Count;
    public bool IsConversation => Thread.Count > 1;
    public MailAccount Account => account;
    public bool ShowMailbox => showMailbox;

    private bool IsOutgoing => Summary.Folder.EndsWith("Sent", StringComparison.OrdinalIgnoreCase)
        || Summary.Folder.EndsWith("Drafts", StringComparison.OrdinalIgnoreCase);

    public string Who => IsOutgoing
        ? "To " + (Summary.To.Count == 0 ? "nobody yet" : string.Join(", ", Summary.To.Select(p => p.Display)))
        : Summary.From?.Display ?? "Unknown sender";

    public string Subject => string.IsNullOrWhiteSpace(Summary.Subject) ? "(no subject)" : Summary.Subject;
    public string Preview => Summary.Preview;
    public bool IsUnread => Thread.Any(m => !m.Seen);
    public bool IsImportant => Thread.Any(m => m.Flagged);
    public bool HasAttachments => Thread.Any(m => m.HasAttachments);
    public string MailboxTag => account.Address.Split('@')[0];

    /// <summary>The categories this mail is in, shown as coloured dots.</summary>
    public IReadOnlyList<MailCategory> Dots => MailCategories.Of(owner.CategoryList, Summary);
    public bool HasDots => Dots.Count > 0;
    public string DotsTip => string.Join(", ", Dots.Select(c => c.Name));

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

    internal void Update(MailSummary updated) => Update(new Dictionary<(string, string, uint), MailSummary> { [MailViewModel.Key(updated)] = updated });

    internal void Update(IReadOnlyDictionary<(string, string, uint), MailSummary> changed)
    {
        Thread = Thread.Select(m => changed.GetValueOrDefault(MailViewModel.Key(m), m)).ToList();
        Summary = changed.GetValueOrDefault(MailViewModel.Key(Summary), Summary);
        OnPropertyChanged(nameof(IsUnread));
        OnPropertyChanged(nameof(IsImportant));
        RefreshDots();
    }

    internal void SetThread(IReadOnlyList<MailSummary> thread)
    {
        Thread = thread;
        Summary = thread[^1];
        OnPropertyChanged(string.Empty);
    }

    internal void RefreshDots()
    {
        OnPropertyChanged(nameof(Dots));
        OnPropertyChanged(nameof(HasDots));
        OnPropertyChanged(nameof(DotsTip));
    }
}
