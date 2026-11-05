using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;

namespace Webignite.Desktop.ViewModels;

// Dated notes and linked files on a client's page. Saved straight away, like the checklist.
public partial class ClientDetailViewModel
{
    public ObservableCollection<ClientNoteRow> LogEntries { get; } = [];
    public ObservableCollection<ClientFileRow> FileRows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLogEntryCommand))]
    public partial string NewLogText { get; set; } = "";

    [ObservableProperty] public partial string RecordsMessage { get; set; } = "";

    public bool HasLog => LogEntries.Count > 0;
    public bool HasFiles => FileRows.Count > 0;

    private void LoadRecords()
    {
        var today = Today;
        LogEntries.Clear();
        foreach (var note in client.Log)
            LogEntries.Add(new ClientNoteRow(note, ClientRecords.Day(note.Date, today), this));
        FileRows.Clear();
        foreach (var file in client.Files)
            FileRows.Add(new ClientFileRow(file, this));
        OnPropertyChanged(nameof(HasLog));
        OnPropertyChanged(nameof(HasFiles));
        LoadFolder();
        LoadNoteLinks();
    }

    // Notes and files are changed on the client as stored now, so unsaved typing in the details stays typed
    // and nothing saved meanwhile is lost.
    /// <summary>A file's folder in the app's Files, with the file picked; or a folder itself.</summary>
    public void ShowInFiles(string path, bool isFolder = false)
    {
        if (isFolder)
            services.Go.Folder(path);
        else
            services.Go.ShowFile(path);
    }

    private void SaveRecords(Func<Client, Client> change)
    {
        client = services.Clients.Change(client.Id, change) ?? client;
        LoadRecords();
    }

    private bool CanAddLogEntry() => IsSaved && !string.IsNullOrWhiteSpace(NewLogText);

    [RelayCommand(CanExecute = nameof(CanAddLogEntry))]
    private void AddLogEntry()
    {
        try
        {
            SaveRecords(c => ClientRecords.AddNote(c, NewLogText, Today));
            NewLogText = "";
            RecordsMessage = "";
        }
        catch (ArgumentException e)
        {
            RecordsMessage = e.Message;
        }
    }

    /// <summary>Removes a note at once, with a note in the corner to undo it.</summary>
    internal void RemoveLogEntry(string id)
    {
        if (client.Log.FirstOrDefault(n => n.Id == id) is not { } note)
            return;
        SaveRecords(c => ClientRecords.RemoveNote(c, id));
        services.OfferUndo("Removed a note", () => SaveRecords(c => ClientRecords.PutBack(c, note)));
    }


    /// <summary>Links files to the client. They stay where they are; only the link is kept.</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        if (!IsSaved)
            return;
        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            RecordsMessage = "Drop files, not folders.";
            return;
        }
        SaveRecords(c => ClientRecords.AddFiles(c, files, Today));
        RecordsMessage = "";
    }

    /// <summary>Unlinks a file at once (the file itself stays), with a note in the corner to undo it.</summary>
    internal void RemoveFile(string path)
    {
        if (client.Files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)) is not { } file)
            return;
        SaveRecords(c => ClientRecords.RemoveFile(c, path));
        services.OfferUndo($"Unlinked {file.Name}", () => SaveRecords(c => ClientRecords.PutBack(c, file)));
    }

    internal void Say(string message) => RecordsMessage = message;
}

public sealed partial class ClientNoteRow(ClientNote note, string day, ClientDetailViewModel owner) : ViewModelBase
{
    public string Day => day;
    public string Text => note.Text;

    [RelayCommand]
    private void Remove() => owner.RemoveLogEntry(note.Id);
}

public sealed partial class ClientFileRow(ClientFile file, ClientDetailViewModel owner) : ViewModelBase
{
    public string Name => file.Name;
    public string Detail => Missing ? "Moved or deleted. Remove it, or drop it here again from its new place."
        : $"{ClientRecords.Kind(file.Path)}, added {file.Added:d MMM yyyy}";
    public bool Missing => !File.Exists(file.Path);

    [RelayCommand]
    private void Open()
    {
        if (Missing)
            return;
        try
        {
            AppServices.OpenFile(file.Path);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            owner.Say("Windows has no program set to open " + Name + ".");
        }
    }

    [RelayCommand]
    private void ShowInFolder()
    {
        if (!Missing)
            owner.ShowInFiles(file.Path);
        else if (Path.GetDirectoryName(file.Path) is { } folder && Directory.Exists(folder))
            owner.ShowInFiles(folder, isFolder: true);
    }

    [RelayCommand]
    private void Remove() => owner.RemoveFile(file.Path);
}
