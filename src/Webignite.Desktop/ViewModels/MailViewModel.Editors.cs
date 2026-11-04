using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>Making, renaming and removing your own folders and categories. The editor shows at the foot of the folder list.</summary>
public partial class MailViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCategoryEditor))]
    public partial CategoryEditorViewModel? CategoryEditor { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolderEditor))]
    public partial FolderEditorViewModel? FolderEditor { get; set; }

    public bool HasCategoryEditor => CategoryEditor is not null;
    public bool HasFolderEditor => FolderEditor is not null;

    [RelayCommand]
    private void NewCategory() => StartNewCategory([]);

    /// <summary>Opens the category editor. Mail given here goes into the new category once it is saved.</summary>
    public void StartNewCategory(IReadOnlyList<MailSummary> applyTo)
    {
        FolderEditor = null;
        CategoryEditor = new CategoryEditorViewModel(this, null, MailCategories.NextColour(categories), applyTo);
    }

    public void StartEditCategory(MailCategory category)
    {
        FolderEditor = null;
        CategoryEditor = new CategoryEditorViewModel(this, category, category.Colour, []);
    }

    internal async Task SaveCategory(CategoryEditorViewModel editor)
    {
        if (MailCategories.Check(categories, editor.Name, editor.Editing?.Keyword) is { } problem)
        {
            editor.Problem = problem;
            return;
        }
        var saved = editor.Editing is { } old
            ? MailCategories.Change(categories, old.Keyword, editor.Name, editor.Colour)
            : MailCategories.Add(categories, editor.Name, editor.Colour);
        services.Settings.MailCategories = saved;
        var keyword = editor.Editing?.Keyword ?? saved[^1].Keyword;
        CategoryEditor = null;
        CategoriesChanged(keyword);
        if (editor.ApplyTo.Count > 0)
            await SetCategory(keyword, true, editor.ApplyTo);
    }

    internal void RemoveCategory(CategoryEditorViewModel editor)
    {
        if (editor.Editing is not { } old)
            return;
        services.Settings.MailCategories = MailCategories.Remove(categories, old.Keyword);
        CategoryEditor = null;
        if (CurrentCategory?.Keyword == old.Keyword)
            CurrentCategory = null;
        CategoriesChanged(null);
        if (CurrentCategory is null && CurrentFolder is null)
            Reload();
        SetStatus($"Removed the category {old.Name}. The mail in it is still where it was.", false);
    }

    private void CategoriesChanged(string? keepOpen)
    {
        var current = CurrentCategory?.Keyword ?? keepOpen;
        var showing = CurrentCategory is not null;
        LoadCategories();
        CurrentCategory = showing ? Categories.FirstOrDefault(c => c.Keyword == current) : null;
        MarkCurrent();
        foreach (var row in Messages)
            row.RefreshDots();
        Reader?.RefreshCategories();
        OnPropertyChanged(nameof(ListTitle));
    }

    public void StartNewFolder(MailAccount mailbox, FolderNodeViewModel? parent)
    {
        CategoryEditor = null;
        FolderEditor = new FolderEditorViewModel(this, FolderEditorMode.New, mailbox, parent);
    }

    public void StartRenameFolder(FolderNodeViewModel folder)
    {
        CategoryEditor = null;
        FolderEditor = new FolderEditorViewModel(this, FolderEditorMode.Rename, folder.Mailbox, folder);
    }

    public void StartRemoveFolder(FolderNodeViewModel folder)
    {
        CategoryEditor = null;
        FolderEditor = new FolderEditorViewModel(this, FolderEditorMode.Remove, folder.Mailbox, folder);
    }

    internal async Task SaveFolder(FolderEditorViewModel editor)
    {
        var folders = services.Store.ListMailFolders(editor.Mailbox.Id);
        var account = editor.Mailbox;
        switch (editor.Mode)
        {
            case FolderEditorMode.New:
            {
                var parentPath = editor.Folder?.Path ?? folders.FirstOrDefault(f => f.Role == FolderRole.Inbox)?.Path ?? "INBOX";
                if (FolderNames.Check(editor.Name, FolderNames.Children(folders, parentPath)) is { } problem)
                {
                    editor.Problem = problem;
                    return;
                }
                MailFolder? made = null;
                var ok = await Run(async () => made = await services.Mail.CreateFolderAsync(account, editor.Folder?.Path, editor.Name),
                    quiet: true, onError: e => editor.Problem = e);
                if (!ok || made is null)
                    return;
                FolderEditor = null;
                Reload();
                SetStatus($"Made the folder {made.Name}.", false);
                break;
            }
            case FolderEditorMode.Rename when editor.Folder is { } folder:
            {
                var siblings = FolderNames.Children(folders, FolderNames.Parent(folder.Path));
                if (FolderNames.Check(editor.Name, siblings, folder.Name) is { } problem)
                {
                    editor.Problem = problem;
                    return;
                }
                if (editor.Name.Trim() == folder.Name)
                {
                    FolderEditor = null;
                    return;
                }
                if (!await Run(() => services.Mail.RenameFolderAsync(account, folder.Path, editor.Name), quiet: true, onError: e => editor.Problem = e))
                    return;
                FolderEditor = null;
                Reload();
                SetStatus($"Renamed {folder.Name} to {editor.Name.Trim()}.", false);
                break;
            }
            case FolderEditorMode.Remove when editor.Folder is { } folder:
            {
                if (!await Run(() => services.Mail.DeleteFolderAsync(account, folder.Path), quiet: true, onError: e => editor.Problem = e))
                    return;
                FolderEditor = null;
                if (CurrentFolder?.Key == folder.Key)
                    CurrentFolder = null;
                Reload();
                SetStatus($"Removed the folder {folder.Name}.", false);
                break;
            }
        }
    }

    internal void CloseEditors()
    {
        CategoryEditor = null;
        FolderEditor = null;
    }
}

public partial class CategoryEditorViewModel : ViewModelBase
{
    private readonly MailViewModel owner;

    public CategoryEditorViewModel(MailViewModel owner, MailCategory? editing, string colour, IReadOnlyList<MailSummary> applyTo)
    {
        this.owner = owner;
        Editing = editing;
        ApplyTo = applyTo;
        Name = editing?.Name ?? "";
        Colours = MailCategories.Colours.Select(c => new ColourChoiceViewModel(c, this)).ToList();
        Colour = colour;
    }

    public MailCategory? Editing { get; }
    public IReadOnlyList<MailSummary> ApplyTo { get; }
    public IReadOnlyList<ColourChoiceViewModel> Colours { get; }
    public bool IsEditing => Editing is not null;

    public string Heading => Editing is null
        ? ApplyTo.Count switch
        {
            0 => "New category",
            1 => "New category for this mail",
            var n => $"New category for {n} messages",
        }
        : "Edit category";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string Problem { get; set; } = "";

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string Colour { get; set; } = "";
    [ObservableProperty] public partial bool ConfirmingRemove { get; set; }

    public bool HasProblem => Problem.Length > 0;

    partial void OnNameChanged(string value) => Problem = "";

    partial void OnColourChanged(string value)
    {
        foreach (var choice in Colours)
            choice.IsChosen = choice.Colour == value;
    }

    [RelayCommand]
    private Task Save() => owner.SaveCategory(this);

    [RelayCommand]
    private void Cancel() => owner.CloseEditors();

    [RelayCommand]
    private void AskRemove() => ConfirmingRemove = true;

    [RelayCommand]
    private void KeepIt() => ConfirmingRemove = false;

    [RelayCommand]
    private void Remove() => owner.RemoveCategory(this);
}

public partial class ColourChoiceViewModel(string colour, CategoryEditorViewModel editor) : ViewModelBase
{
    public string Colour => colour;
    public string Tip => char.ToUpperInvariant(colour[0]) + colour[1..];

    [ObservableProperty] public partial bool IsChosen { get; set; }

    [RelayCommand]
    private void Pick() => editor.Colour = colour;
}

public enum FolderEditorMode
{
    New,
    Rename,
    Remove,
}

public partial class FolderEditorViewModel(MailViewModel owner, FolderEditorMode mode, MailAccount mailbox, FolderNodeViewModel? folder) : ViewModelBase
{
    public FolderEditorMode Mode => mode;
    public MailAccount Mailbox => mailbox;

    /// <summary>The folder being renamed or removed, or the one a new folder goes inside (none means Inbox).</summary>
    public FolderNodeViewModel? Folder => folder;

    public bool AsksName => mode != FolderEditorMode.Remove;

    public string Heading => mode switch
    {
        FolderEditorMode.New => $"New folder in {folder?.Name ?? "Inbox"}",
        FolderEditorMode.Rename => $"Rename {folder?.Name}",
        _ => $"Remove {folder?.Name}?",
    };

    public string Note => mode switch
    {
        FolderEditorMode.New => $"Made on the mail server for {mailbox.Address}, so it shows on your phone too.",
        FolderEditorMode.Rename => "Renamed on the mail server too. The mail in it stays.",
        _ => "This takes it off the mail server. Only an empty folder can be removed.",
    };

    public string SaveLabel => mode switch
    {
        FolderEditorMode.New => "Make folder",
        FolderEditorMode.Rename => "Rename",
        _ => "Remove it",
    };

    [ObservableProperty] public partial string Name { get; set; } = mode == FolderEditorMode.Rename ? folder?.Name ?? "" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string Problem { get; set; } = "";

    public bool HasProblem => Problem.Length > 0;

    partial void OnNameChanged(string value) => Problem = "";

    [RelayCommand]
    private Task Save() => owner.SaveFolder(this);

    [RelayCommand]
    private void Cancel() => owner.CloseEditors();
}
