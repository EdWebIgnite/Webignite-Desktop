using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Calendar;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Tasks;

namespace Webignite.Desktop.ViewModels;

/// <summary>One client's page: their details, and the quotes, invoices, to-dos and events that carry their name.</summary>
public partial class ClientDetailViewModel : ViewModelBase
{
    private readonly AppServices services;
    private readonly Action close;
    private Client client;
    private bool isNew;

    public ClientDetailViewModel(Client client, bool isNew, AppServices services, Action close)
    {
        this.client = client;
        this.isNew = isNew;
        this.services = services;
        this.close = close;
        Name = client.Name;
        ContactPerson = client.ContactPerson ?? "";
        Email = client.Email ?? "";
        Phone = client.Phone ?? "";
        Website = client.Website ?? "";
        Address = client.Address ?? "";
        Notes = client.Notes ?? "";
        Refresh();
    }

    public string Title => isNew ? "New client" : client.Name;
    public bool IsSaved => !isNew;

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string ContactPerson { get; set; } = "";
    [ObservableProperty] public partial string Email { get; set; } = "";
    [ObservableProperty] public partial string Phone { get; set; } = "";
    [ObservableProperty] public partial string Website { get; set; } = "";
    [ObservableProperty] public partial string Address { get; set; } = "";
    [ObservableProperty] public partial string Notes { get; set; } = "";
    [ObservableProperty] public partial string SaveState { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwed))]
    public partial string Owed { get; set; } = "";

    public bool HasOwed => Owed.Length > 0;
    public bool HasProblems => Problems.Count > 0;
    [ObservableProperty] public partial bool ConfirmingDelete { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    public partial string NewTask { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingClient))]
    public partial DocumentEditorViewModel? Editor { get; set; }

    public bool IsShowingClient => Editor is null;

    public ObservableCollection<string> Problems { get; } = [];
    public ObservableCollection<DocumentRow> Documents { get; } = [];
    public ObservableCollection<ClientTaskViewModel> Tasks { get; } = [];
    public ObservableCollection<ClientEventViewModel> Events { get; } = [];

    public bool HasDocuments => Documents.Count > 0;
    public bool HasTasks => Tasks.Count > 0;
    public bool HasEvents => Events.Count > 0;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private Client Edited() => client with
    {
        Name = Name.Trim(),
        ContactPerson = Blank(ContactPerson),
        Email = Blank(Email),
        Phone = Blank(Phone),
        Website = Blank(Website),
        Address = Blank(Address),
        Notes = Blank(Notes),
        Created = client.Created == default ? DateTimeOffset.UtcNow : client.Created,
    };

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [RelayCommand]
    private void Save() => TrySave();

    private bool TrySave()
    {
        var edited = Edited();
        Problems.Clear();
        foreach (var problem in ClientDirectory.Check(edited, services.Store.ListClients()))
            Problems.Add(problem);
        OnPropertyChanged(nameof(HasProblems));
        if (Problems.Count > 0)
        {
            SaveState = "";
            return false;
        }

        var renamed = !isNew && ClientDirectory.Key(client.Name) != ClientDirectory.Key(edited.Name);
        client = services.Store.SaveClient(edited);
        isNew = false;
        SaveState = renamed
            ? "Saved. Quotes and invoices are matched by name, so older ones under the old name no longer show here."
            : "Saved.";
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsSaved));
        Refresh();
        return true;
    }

    [RelayCommand]
    private void Back()
    {
        // A brand new client with nothing typed is simply dropped.
        if (isNew && string.IsNullOrWhiteSpace(Name))
        {
            close();
            return;
        }
        if (TrySave())
            close();
    }

    [RelayCommand]
    private void AskDelete() => ConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => ConfirmingDelete = false;

    [RelayCommand]
    private void Delete()
    {
        // Only the client page goes. Quotes, invoices and to-dos stay as they are.
        services.Store.DeleteClient(client.Id);
        close();
    }

    [RelayCommand]
    private void NewQuote() => StartDocument(DocumentKind.Quote);

    [RelayCommand]
    private void NewInvoice() => StartDocument(DocumentKind.Invoice);

    private void StartDocument(DocumentKind kind)
    {
        if (!TrySave())
            return;
        var editor = new DocumentEditorViewModel(kind, services, CloseEditor);
        editor.CustomerName = client.Name;
        Editor = editor;
    }

    [RelayCommand]
    private void OpenDocument(DocumentRow row) => Editor = new DocumentEditorViewModel(row.Stored, services, CloseEditor);

    private void CloseEditor()
    {
        Editor = null;
        Refresh();
    }

    private bool CanAddTask() => !string.IsNullOrWhiteSpace(NewTask) && !isNew;

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private void AddTask()
    {
        services.Store.SaveTask(new TaskItem
        {
            Id = TaskItem.NewId(),
            Title = NewTask.Trim(),
            Customer = client.Name,
            Created = DateTimeOffset.UtcNow,
        });
        NewTask = "";
        Refresh();
    }

    public void Finish(TaskItem task)
    {
        services.Store.SaveTask(task with { Done = true, DoneAt = DateTimeOffset.UtcNow });
        Refresh();
    }

    public void Refresh()
    {
        Documents.Clear();
        Tasks.Clear();
        Events.Clear();
        if (!isNew)
        {
            var summary = ClientDirectory.Summarise(client, services.Store.List(), services.Store.ListTasks(),
                services.Store.ListEvents(Today, Today.AddDays(90)), Today);
            foreach (var doc in summary.Documents)
                Documents.Add(new DocumentRow(doc));
            foreach (var task in summary.OpenTasks)
                Tasks.Add(new ClientTaskViewModel(task, this));
            foreach (var ev in summary.UpcomingEvents)
                Events.Add(new ClientEventViewModel(ev));
            Owed = summary.Owed == 0 ? "" : $"{Money.Rand(summary.Owed)} invoiced and not yet marked paid";
        }
        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(HasEvents));
        AddTaskCommand.NotifyCanExecuteChanged();
    }
}

public partial class ClientTaskViewModel(TaskItem task, ClientDetailViewModel owner) : ViewModelBase
{
    public string Title => task.Title;
    public string DueText => task.Due?.ToString("d MMM") ?? "";
    public bool IsOverdue => task.Due is { } due && due < DateOnly.FromDateTime(DateTime.Today);

    [RelayCommand]
    private void Finish() => owner.Finish(task);
}

public sealed class ClientEventViewModel(CalendarEvent ev)
{
    public string Title => ev.Title;
    public string When => ev.Start is { } start ? $"{ev.Date:ddd d MMM}, {start:HH\\:mm}" : ev.Date.ToString("ddd d MMM");
}
