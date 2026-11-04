using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Notes;
using Webignite.Core.Writing;

namespace Webignite.Desktop.ViewModels;

// The open note: its title and writing, saved a moment after typing stops, when another note is opened, and
// when the page or the app closes. Pin, move, link to a client, delete and put back.
public sealed partial class NotesViewModel
{
    private DispatcherTimer? saveTimer;
    private bool loading;
    private bool saving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpen), nameof(NoOpen), nameof(OpenIsDeleted), nameof(CanEdit), nameof(PinText), nameof(PlaceText))]
    public partial Note? Open { get; private set; }

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial RichDocument? Document { get; private set; }
    [ObservableProperty] public partial int Revision { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoldsWork))]
    public partial bool Dirty { get; private set; }

    [ObservableProperty] public partial string SavedText { get; private set; } = "";

    /// <summary>The client the open note is about; the picker is one-way and calls <see cref="ClientPicked"/>.</summary>
    [ObservableProperty] public partial Client? OpenClient { get; private set; }

    public ObservableCollection<NoteMoveTarget> MoveTargets { get; } = [];

    public bool HoldsWork => Dirty;
    public bool HasOpen => Open is not null;
    public bool NoOpen => Open is null;
    public bool OpenIsDeleted => Open?.Deleted is not null;
    public bool CanEdit => Open is { Deleted: null };
    public string PinText => Open?.Pinned == true ? "Unpin" : "Pin to the top";
    private string? OpenClientId => Open?.ClientId;

    /// <summary>"My notes", or "My notes › Meetings".</summary>
    public string PlaceText => Open is not { } note ? ""
        : (notebooks.FirstOrDefault(b => b.Id == note.NotebookId)?.Name ?? "") + (note.Folder.Length > 0 ? " › " + note.Folder : "");

    private void Show(Note? note)
    {
        StopSaveTimer();
        loading = true;
        Open = note;
        Title = note?.Title ?? "";
        Document = note is null ? null : services.Notes.Writing(note.Id);
        Revision = 0;
        Dirty = false;
        SavedText = "";
        OpenClient = note?.ClientId is { } id ? Clients.FirstOrDefault(c => c.Id == id) : null;
        listing = true;
        Picked = note is null ? null : Rows.FirstOrDefault(r => r.Note.Id == note.Id);
        listing = false;
        loading = false;
        OnPropertyChanged(nameof(TabTitle));
    }

    partial void OnTitleChanged(string value) => Typed();

    partial void OnRevisionChanged(int value) => Typed();

    private void Typed()
    {
        if (loading || Open is null)
            return;
        Dirty = true;
        SavedText = "";
        saveTimer ??= NewSaveTimer();
        saveTimer.Stop();
        saveTimer.Start();
    }

    private DispatcherTimer NewSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Keep();
        };
        return timer;
    }

    private void StopSaveTimer() => saveTimer?.Stop();

    /// <summary>Saves the open note if anything was typed. Called before another note opens and when the app closes.</summary>
    public void Keep()
    {
        StopSaveTimer();
        if (!Dirty || Open is not { } open || Document is not { } writing)
            return;
        saving = true;
        try
        {
            if (services.Notes.Save(open.Id, Title, writing) is not { } saved)
            {
                Message = "This note was deleted for good elsewhere, so what was typed couldn't be kept.";
                return;
            }
            Open = saved;
            Dirty = false;
            SavedText = "Saved";
            all = [.. all.Select(n => n.Id == saved.Id ? saved : n)];
            // The row is updated where it is, so the list doesn't jump while typing.
            var at = Rows.ToList().FindIndex(r => r.Note.Id == saved.Id);
            if (at >= 0)
            {
                listing = true;
                Rows[at] = Row(saved, NoteRules.Preview(saved), DateTimeOffset.Now);
                Picked = Rows[at];
                listing = false;
            }
            OnPropertyChanged(nameof(TabTitle));
        }
        finally
        {
            saving = false;
        }
    }

    // The open note changed in another window: take it up when nothing is waiting to be saved here.
    private void OpenChangedElsewhere(string id)
    {
        if (services.Notes.Get(id) is not { } fresh)
        {
            Show(null);
            return;
        }
        if (Dirty)
        {
            Open = Open! with { Pinned = fresh.Pinned, Folder = fresh.Folder, NotebookId = fresh.NotebookId, ClientId = fresh.ClientId, Deleted = fresh.Deleted };
            return;
        }
        if (fresh.Changed != Open?.Changed)
            Show(fresh);
        else
            Open = fresh;
    }

    // Changes to the open note's place and links, saved straight away.
    private void ChangeOpen(Func<Note, Note> change)
    {
        if (Open is not { } open)
            return;
        Keep();
        saving = true;
        try
        {
            if (services.Notes.Change(open.Id, change) is { } changed)
                Open = changed;
        }
        finally
        {
            saving = false;
        }
        Reload();
    }

    [RelayCommand]
    private void TogglePin() => ChangeOpen(n => n with { Pinned = !n.Pinned });

    public void ClientPicked(Client? client)
    {
        if (loading || Open is null || client?.Id == Open.ClientId)
            return;
        OpenClient = client;
        ChangeOpen(n => n with { ClientId = client?.Id });
    }

    [RelayCommand]
    private void ForgetClient() => ClientPicked(null);

    public void MoveTo(NoteMoveTarget target) => ChangeOpen(n => n with { NotebookId = target.NotebookId, Folder = target.Folder });

    [RelayCommand]
    private void DeleteOpen()
    {
        if (Open is not { } open)
            return;
        Keep();
        var name = NoteRules.Name(open);
        saving = true;
        try
        {
            services.Notes.Delete(open.Id);
        }
        finally
        {
            saving = false;
        }
        var at = Rows.ToList().FindIndex(r => r.Note.Id == open.Id);
        Reload();
        Show(Rows.Count == 0 ? null : Rows[Math.Clamp(at, 0, Rows.Count - 1)].Note);
        services.OfferUndo($"{name} is in Deleted.", () =>
        {
            services.Notes.Restore(open.Id);
            OpenNote(open.Id);
        });
    }

    [RelayCommand]
    private void RestoreOpen()
    {
        if (Open is not { } open)
            return;
        services.Notes.Restore(open.Id);
        Reload();
        OpenNote(open.Id);
    }

    [RelayCommand]
    private void PurgeOpen()
    {
        if (Open is not { } open)
            return;
        services.Notes.Purge(open.Id);
        Show(null);
        Reload();
    }

    private void RefreshMoveTargets()
    {
        MoveTargets.Clear();
        foreach (var notebook in notebooks)
        {
            MoveTargets.Add(new NoteMoveTarget(notebook.Name, notebook.Id, ""));
            foreach (var folder in notebook.Folders)
                MoveTargets.Add(new NoteMoveTarget($"{notebook.Name} › {folder}", notebook.Id, folder));
        }
        OnPropertyChanged(nameof(PlaceText));
    }
}

/// <summary>A notebook, or a folder in one, to move a note to.</summary>
public sealed record NoteMoveTarget(string Label, string NotebookId, string Folder);
