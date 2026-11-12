using System.Collections.ObjectModel;
using Avalonia.Controls.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

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

    /// <summary>Folded up: only the name and Inbox show, and the folder open now. Kept in the setting <c>mail-folded</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(ShowsNewFolder))]
    public partial bool IsFolded { get; set; }

    public bool IsOpen => !IsFolded;

    /// <summary>New folder shows on an open mailbox only.</summary>
    public bool ShowsNewFolder => HasTitle && !IsFolded;

    partial void OnIsFoldedChanged(bool value) => ShowFolders();

    /// <summary>Hides or shows the folders for the fold, keeping Inbox and the folder open now in sight.</summary>
    public void ShowFolders()
    {
        foreach (var folder in Folders)
            folder.IsHidden = !MailboxFolding.Shows(IsFolded, folder.Role, folder.IsCurrent);
    }

    [RelayCommand]
    private void ToggleFold()
    {
        if (Account is not null)
            owner.FoldMailbox(this);
    }

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
            owner.EditorsPart.StartNewFolder(Account, null);
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

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task MarkAllRead() => owner.MarkFolderRead(this);

    /// <summary>Only folders you made can be renamed or removed.</summary>
    public bool CanChange => Kind == FolderKind.Folder && FolderNames.CanChange(Folder);

    /// <summary>Unread counts only show where they help: inboxes and your own folders, not Sent or Trash.</summary>
    public string UnreadText => Unread > 0 && Role is not (FolderRole.Sent or FolderRole.Trash or FolderRole.Drafts or FolderRole.Junk) ? Unread.ToString() : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadText))]
    public partial int Unread { get; set; }

    /// <summary>Out of sight while its mailbox is folded up.</summary>
    [ObservableProperty] public partial bool IsHidden { get; set; }

    [ObservableProperty] public partial bool IsCurrent { get; set; }

    /// <summary>Lit up while mail is dragged over it.</summary>
    [ObservableProperty] public partial bool IsDropTarget { get; set; }

    [RelayCommand]
    private void Open() => owner.Open(this);

    [RelayCommand]
    private void NewFolder()
    {
        if (Kind == FolderKind.Folder)
            owner.EditorsPart.StartNewFolder(Mailbox, this);
    }

    [RelayCommand]
    private void Rename()
    {
        if (CanChange)
            owner.EditorsPart.StartRenameFolder(this);
    }

    [RelayCommand]
    private void Remove()
    {
        if (CanChange)
            owner.EditorsPart.StartRemoveFolder(this);
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
    private void Edit() => owner.EditorsPart.StartEditCategory(category);
}
