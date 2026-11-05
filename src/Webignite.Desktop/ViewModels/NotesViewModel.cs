using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core;
using Webignite.Core.Clients;
using Webignite.Core.Notes;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Notes: notebooks and their folders on the left, the notes in the one picked (or found by a search) in the
/// middle, and the open note on the right, saved as it is typed. A note can be about a client, and then it
/// shows on their page too.
/// </summary>
public sealed partial class NotesViewModel : ViewModelBase, IHoldsWork, IDisposable, IRefreshable, ITabTitle, IHasPanes
{
    private readonly AppServices services;
    private IReadOnlyList<Note> all = [];
    private IReadOnlyList<Notebook> notebooks = [];
    private bool listing;

    public NotesViewModel(AppServices services)
    {
        this.services = services;
        Panels = new PanelsViewModel(services.Settings, "notes", [("notebooks", "Notebooks"), ("notes", "Notes list"), ("note", "Open note")], spine: "note");
        services.Notes.Changed += NotesChanged;
        LoadClients();
        Reload();
        PickPlace(Places.FirstOrDefault(p => p.Kind == NotePlace.Notebook) ?? Places[0]);
    }

    /// <summary>Notebooks, the list and the open note: shown, hidden, moved and sized like Mail's panes.</summary>
    public PanelsViewModel Panels { get; }

    public ObservableCollection<NotePlaceRow> Places { get; } = [];
    public ObservableCollection<NoteRow> Rows { get; } = [];
    public ObservableCollection<Client> Clients { get; } = [];

    /// <summary>Where the list is looking: all notes, a notebook, a folder, a client's, or Deleted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListTitle), nameof(InDeleted), nameof(CanAddFolder), nameof(CanRemoveNotebook), nameof(InFolder), nameof(ListEmptyText))]
    public partial NotePlaceRow? Place { get; private set; }

    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial NoteRow? Picked { get; private set; }
    [ObservableProperty] public partial string Message { get; private set; } = "";

    /// <summary>A box asking for a name: a new folder, or a new name for a folder or notebook.</summary>
    [ObservableProperty] public partial bool AskingName { get; private set; }
    [ObservableProperty] public partial string NameAsked { get; private set; } = "";
    [ObservableProperty] public partial string NameTyped { get; set; } = "";
    private Action<string>? nameDone;

    public bool Searching => Query.Trim().Length > 0;
    public bool InDeleted => Place?.Kind == NotePlace.Deleted;
    public bool InFolder => Place?.Kind == NotePlace.Folder;
    public bool CanAddFolder => Place?.Kind is NotePlace.Notebook or NotePlace.Folder;
    public bool CanRemoveNotebook => Place?.Kind == NotePlace.Notebook;
    public bool HasRows => Rows.Count > 0;
    public bool NoRows => Rows.Count == 0;
    public string? TabTitle => Open is { } open ? NoteRules.Name(open) : null;

    public string ListTitle => Searching ? "Found" : Place?.Title ?? "Notes";

    public string ListEmptyText => Searching ? "No note has those words."
        : InDeleted ? "Nothing deleted. Deleted notes wait here for 30 days."
        : "No notes here yet. Press New note, or Ctrl+Alt+N in any program.";

    // Lists

    /// <summary>Reads notes and notebooks again and redraws both lists, keeping the place and the note picked.</summary>
    private void Reload()
    {
        notebooks = services.Notes.Notebooks();
        all = services.Notes.List();
        var place = Place;
        Places.Clear();
        Places.Add(new NotePlaceRow(NotePlace.All, "All notes", Count: all.Count(n => n.Deleted is null)));
        if (place is { Kind: NotePlace.Client } client)
            Places.Add(client with { Count = NoteRules.In(all, NotePlace.Client, clientId: client.ClientId).Count() });
        foreach (var notebook in notebooks)
        {
            Places.Add(new NotePlaceRow(NotePlace.Notebook, notebook.Name, notebook.Id, Count: NoteRules.In(all, NotePlace.Notebook, notebook.Id).Count()));
            foreach (var folder in notebook.Folders)
                Places.Add(new NotePlaceRow(NotePlace.Folder, folder, notebook.Id, folder, Count: NoteRules.In(all, NotePlace.Folder, notebook.Id, folder).Count()));
        }
        Places.Add(new NotePlaceRow(NotePlace.Deleted, "Deleted", Count: all.Count(n => n.Deleted is not null)));
        Place = place is null ? null : Places.FirstOrDefault(p => p.Same(place)) ?? Places[0];
        ShowRows();
        RefreshMoveTargets();
    }

    private void ShowRows()
    {
        listing = true;
        var openId = Open?.Id;
        Rows.Clear();
        var now = DateTimeOffset.Now;
        if (Searching)
        {
            foreach (var hit in NoteRules.Search(all.Where(n => n.Deleted is null), Query))
                Rows.Add(Row(hit.Note, hit.Snippet, now));
        }
        else if (Place is { } place)
        {
            foreach (var note in NoteRules.Ordered(NoteRules.In(all, place.Kind, place.NotebookId, place.Folder, place.ClientId)))
                Rows.Add(Row(note, NoteRules.Preview(note), now));
        }
        Picked = Rows.FirstOrDefault(r => r.Note.Id == openId);
        listing = false;
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(NoRows));
        OnPropertyChanged(nameof(ListTitle));
        OnPropertyChanged(nameof(ListEmptyText));
    }

    private NoteRow Row(Note note, string preview, DateTimeOffset now)
    {
        var where = Place?.Kind is NotePlace.All or NotePlace.Client || Searching
            ? notebooks.FirstOrDefault(b => b.Id == note.NotebookId)?.Name
            : null;
        var client = note.ClientId is { } id ? Clients.FirstOrDefault(c => c.Id == id)?.Name : null;
        var detail = string.Join(" · ", new[] { When(note.Changed, now), NoteRules.Progress(note), client, where }.Where(s => !string.IsNullOrEmpty(s)));
        return new NoteRow(note, NoteRules.Name(note), preview, detail);
    }

    /// <summary>A time today, otherwise the day in words. Times follow the PC's region.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var day = DateOnly.FromDateTime(local.DateTime);
        return day == today ? local.ToString("t", CultureInfo.CurrentCulture) : Wording.Day(day, today);
    }

    public void LoadClients()
    {
        var fresh = services.Clients.List();
        Clients.Clear();
        foreach (var client in fresh)
            Clients.Add(client);
        OpenClient = OpenClientId is { } id ? Clients.FirstOrDefault(c => c.Id == id) : null;
    }

    public void Refresh()
    {
        LoadClients();
        Reload();
    }

    // Another page (or the quick note) changed notes: show it. The note being typed in isn't reloaded under the person.
    private void NotesChanged(string? id)
    {
        if (saving)
            return;
        Reload();
        if (Open is { } open && (id is null || id == open.Id))
            OpenChangedElsewhere(open.Id);
    }

    public void PickPlace(NotePlaceRow? place)
    {
        if (place is null || listing)
            return;
        Keep();
        Query = "";
        Place = place;
        ShowRows();
        if (Picked is null && !InDeleted)
            Show(Rows.FirstOrDefault()?.Note);
    }

    /// <summary>The notes about a client, e.g. from their page.</summary>
    public void ShowClient(string clientId, string clientName)
    {
        Keep();
        Query = "";
        Place = new NotePlaceRow(NotePlace.Client, $"About {clientName}", ClientId: clientId);
        Reload();
        Show(Rows.FirstOrDefault()?.Note);
    }

    partial void OnQueryChanged(string value)
    {
        OnPropertyChanged(nameof(Searching));
        ShowRows();
    }

    public void PickRow(NoteRow? row)
    {
        if (listing || row is null || row.Note.Id == Open?.Id)
            return;
        Keep();
        Show(services.Notes.Get(row.Note.Id));
    }

    /// <summary>Opens a note, e.g. from the client page; shows its notebook or folder when it isn't in the list.</summary>
    public void OpenNote(string id)
    {
        Keep();
        if (services.Notes.Get(id) is not { } note)
            return;
        if (Rows.All(r => r.Note.Id != id))
        {
            Query = "";
            Place = Places.FirstOrDefault(p => note.Deleted is not null ? p.Kind == NotePlace.Deleted
                : note.Folder.Length > 0 ? p.Kind == NotePlace.Folder && p.NotebookId == note.NotebookId && string.Equals(p.Folder, note.Folder, StringComparison.OrdinalIgnoreCase)
                : p.Kind == NotePlace.Notebook && p.NotebookId == note.NotebookId) ?? Places[0];
            ShowRows();
        }
        Show(note);
    }

    // Making and moving

    /// <summary>The notebook and folder a new note goes into: the one shown, else the first notebook.</summary>
    private (string Notebook, string Folder, string? Client) Target() => Place switch
    {
        { Kind: NotePlace.Notebook, NotebookId: { } b } => (b, "", null),
        { Kind: NotePlace.Folder, NotebookId: { } b, Folder: { } f } => (b, f, null),
        { Kind: NotePlace.Client, ClientId: { } c } => (notebooks[0].Id, "", c),
        _ => (notebooks[0].Id, "", null),
    };

    [RelayCommand]
    private void NewNote()
    {
        Keep();
        var (notebook, folder, client) = Target();
        if (InDeleted || Searching)
        {
            Query = "";
            Place = Places.First(p => p.Kind == NotePlace.Notebook && p.NotebookId == notebook);
        }
        var note = services.Notes.New(notebook, folder, client);
        saving = true;
        try
        {
            Reload();
        }
        finally
        {
            saving = false;
        }
        Show(note);
        FocusTitle?.Invoke();
    }

    /// <summary>Asks the view to put the caret in the title, for a new note.</summary>
    public Action? FocusTitle { get; set; }

    [RelayCommand]
    private void AddNotebook()
    {
        var notebook = services.Notes.AddNotebook();
        Reload();
        PickPlace(Places.FirstOrDefault(p => p.Kind == NotePlace.Notebook && p.NotebookId == notebook.Id));
        Ask("Name the notebook", notebook.Name, name =>
        {
            services.Notes.RenameNotebook(notebook.Id, name);
            Reload();
        });
    }

    [RelayCommand]
    private void AddFolder()
    {
        if (Place?.NotebookId is not { } notebookId)
            return;
        Ask("Name the folder", "", name =>
        {
            if (services.Notes.AddFolder(notebookId, name) is not { } made)
            {
                Message = "There's a folder by that name already, or the name is empty.";
                return;
            }
            Reload();
            PickPlace(Places.FirstOrDefault(p => p.Kind == NotePlace.Folder && p.NotebookId == notebookId && p.Folder == made));
        });
    }

    [RelayCommand]
    private void RenamePlace()
    {
        if (Place is not { } place)
            return;
        switch (place.Kind)
        {
            case NotePlace.Notebook:
                Ask("Rename the notebook", place.Title, name =>
                {
                    services.Notes.RenameNotebook(place.NotebookId!, name);
                    Reload();
                });
                break;
            case NotePlace.Folder:
                Ask("Rename the folder", place.Folder!, name =>
                {
                    services.Notes.RenameFolder(place.NotebookId!, place.Folder!, name);
                    Place = place with { Title = name.Trim(), Folder = name.Trim() };
                    Reload();
                });
                break;
        }
    }

    /// <summary>Removes the folder picked; its notes stay in the notebook. Or an empty notebook.</summary>
    [RelayCommand]
    private void RemovePlace()
    {
        if (Place is not { } place)
            return;
        Keep();
        if (place.Kind == NotePlace.Folder)
        {
            services.Notes.RemoveFolder(place.NotebookId!, place.Folder!);
            Message = $"The {place.Folder} folder is gone. Its notes are still in the notebook.";
            Place = Places.First(p => p.Kind == NotePlace.Notebook && p.NotebookId == place.NotebookId);
            Reload();
        }
        else if (place.Kind == NotePlace.Notebook)
        {
            if (!services.Notes.RemoveNotebook(place.NotebookId!))
            {
                Message = notebooks.Count == 1 ? "This is the only notebook, so it stays." : "Move or delete its notes first, then remove the notebook.";
                return;
            }
            Place = null;
            Reload();
            PickPlace(Places.First(p => p.Kind == NotePlace.Notebook));
        }
    }

    [RelayCommand]
    private void EmptyDeleted()
    {
        if (Open is { Deleted: not null })
            Show(null);
        services.Notes.EmptyDeleted();
        Reload();
    }

    private void Ask(string what, string start, Action<string> done)
    {
        NameAsked = what;
        NameTyped = start;
        nameDone = done;
        AskingName = true;
    }

    [RelayCommand]
    private void NameOk()
    {
        var done = nameDone;
        AskingName = false;
        nameDone = null;
        if (NameTyped.Trim().Length > 0)
            done?.Invoke(NameTyped);
    }

    [RelayCommand]
    private void NameCancel()
    {
        AskingName = false;
        nameDone = null;
    }

    public void Dispose()
    {
        Keep();
        services.Notes.Changed -= NotesChanged;
        StopSaveTimer();
    }
}

/// <summary>A line in the list on the left: all notes, a notebook, a folder in it, a client's notes, Deleted.</summary>
public sealed record NotePlaceRow(NotePlace Kind, string Title, string? NotebookId = null, string? Folder = null, string? ClientId = null, int Count = 0)
{
    public bool IsFolder => Kind == NotePlace.Folder;
    public bool IsNotebook => Kind == NotePlace.Notebook;
    public string CountText => Count == 0 ? "" : Count.ToString(CultureInfo.CurrentCulture);
    public string Icon => Kind switch
    {
        NotePlace.Folder => "Folder",
        NotePlace.Deleted => "Trash",
        NotePlace.Client => "Clients",
        NotePlace.All => "Inbox",
        _ => "Notes",
    };

    public bool Same(NotePlaceRow other) =>
        Kind == other.Kind && NotebookId == other.NotebookId && ClientId == other.ClientId
        && string.Equals(Folder, other.Folder, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A note in the middle list.</summary>
public sealed record NoteRow(Note Note, string Name, string Preview, string Detail)
{
    public bool Pinned => Note.Pinned;
    public bool HasPreview => Preview.Length > 0;
}
