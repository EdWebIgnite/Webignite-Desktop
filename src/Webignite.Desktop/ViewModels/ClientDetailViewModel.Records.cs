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
    }

    private void SaveRecords(Client changed)
    {
        var stored = services.Store.ListClients().FirstOrDefault(c => c.Id == client.Id) ?? client;
        client = services.Store.SaveClient(stored with { Log = changed.Log, Files = changed.Files });
        LoadRecords();
    }

    private bool CanAddLogEntry() => IsSaved && !string.IsNullOrWhiteSpace(NewLogText);

    [RelayCommand(CanExecute = nameof(CanAddLogEntry))]
    private void AddLogEntry()
    {
        try
        {
            SaveRecords(ClientRecords.AddNote(client, NewLogText, Today));
            NewLogText = "";
            RecordsMessage = "";
        }
        catch (ArgumentException e)
        {
            RecordsMessage = e.Message;
        }
    }

    internal void RemoveLogEntry(string id) => SaveRecords(ClientRecords.RemoveNote(client, id));

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
        SaveRecords(ClientRecords.AddFiles(client, files, Today));
        RecordsMessage = "";
    }

    internal void RemoveFile(string path) => SaveRecords(ClientRecords.RemoveFile(client, path));

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
        if (OperatingSystem.IsWindows() && !Missing)
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file.Path}\"") { UseShellExecute = false });
        else if (Path.GetDirectoryName(file.Path) is { } folder && Directory.Exists(folder))
            AppServices.OpenFile(folder);
    }

    [RelayCommand]
    private void Remove() => owner.RemoveFile(file.Path);
}
