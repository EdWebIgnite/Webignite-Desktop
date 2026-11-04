using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Calendar;
using Webignite.Core.Clients;
using Webignite.Core.Time;
using Webignite.Core.Documents;
using Webignite.Core.Mail;
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
        HourlyRate = client.HourlyRate is { } rate ? rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";
        Refresh();
    }

    /// <summary>The client's hourly rate as typed, e.g. "650". Blank means none.</summary>
    [ObservableProperty] public partial string HourlyRate { get; set; } = "";

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

    /// <summary>Counts up on each save, so the short "Saved" note shows again.</summary>
    [ObservableProperty] public partial int SavedStamp { get; set; }
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

    // Whatever replaces an open editor, what was typed in it is saved first.
    partial void OnEditorChanging(DocumentEditorViewModel? oldValue, DocumentEditorViewModel? newValue) => oldValue?.Keep();

    public bool IsShowingClient => Editor is null;

    public ObservableCollection<string> Problems { get; } = [];
    public ObservableCollection<DocumentRow> Documents { get; } = [];
    public ObservableCollection<ClientTaskViewModel> Tasks { get; } = [];
    public ObservableCollection<ClientEventViewModel> Events { get; } = [];
    public ObservableCollection<ClientMailRow> Mail { get; } = [];
    public bool HasMail => Mail.Count > 0;
    public bool HasMailboxes => services.Store.ListMailAccounts().Count > 0;
    public bool CanWriteMail => HasMailboxes && Recipients.Parse(Email).People.Count > 0;
    public string MailEmptyText => !HasMailboxes ? "Add a mailbox under Mail to see mail with this client here."
        : new ClientMail(client).IsEmpty ? "Add an email address or website above to find their mail."
        : "No mail with them on this PC yet.";

    [RelayCommand]
    private void WriteMail() => services.Go.WriteMail(Recipients.Parse(Email).People);

    /// <summary>Builds this month's statement from Xero and opens it in Mail.</summary>
    [RelayCommand]
    private async Task DraftStatement()
    {
        DraftingStatement = true;
        try
        {
            var (message, problem) = await Statementing.DraftAsync(services, client.Name);
            if (message is not null)
                services.ShowNote?.Invoke(problem ? "Couldn't draft it" : "Nothing owed", message, null);
        }
        finally
        {
            DraftingStatement = false;
        }
    }

    [ObservableProperty] public partial bool DraftingStatement { get; set; }

    [RelayCommand]
    private void OpenMail(ClientMailRow row) => services.Go.Mail(row.Summary);

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
        HourlyRate = TimeRules.TryParseRate(HourlyRate, out var rate) ? rate : client.HourlyRate,
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
        if (!TimeRules.TryParseRate(HourlyRate, out _))
            Problems.Add("The hourly rate doesn't look right. Type it in Rand, e.g. 650, or leave it empty.");
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
            ? "Quotes and invoices are matched by name, so older ones under the old name no longer show here."
            : "";
        SavedStamp++;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsSaved));
        OnPropertyChanged(nameof(CanStartOnboarding));
        AddLogEntryCommand.NotifyCanExecuteChanged();
        Refresh();
        return true;
    }

    [RelayCommand]
    private void Back()
    {
        if (Leave())
            close();
    }

    /// <summary>
    /// Gets ready to close or switch to another client: saves what was typed. A brand new client with nothing
    /// typed is simply dropped. Returns false when something needs fixing first; the problems are showing.
    /// </summary>
    internal bool Leave()
    {
        Editor?.Keep();
        return (isNew && string.IsNullOrWhiteSpace(Name)) || TrySave();
    }

    /// <summary>The client this page is for, so the list can mark it as the open one.</summary>
    internal string ClientId => client.Id;

    [RelayCommand]
    private void AskDelete() => ConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => ConfirmingDelete = false;

    [RelayCommand]
    private void Delete()
    {
        // Only the client page goes. Quotes, invoices and to-dos stay as they are. It asked first (AskDelete),
        // and Undo still brings it back.
        var gone = client;
        services.Store.DeleteClient(gone.Id);
        close();
        services.OfferUndo($"Deleted {gone.Name}", () =>
        {
            services.Store.SaveClient(gone);
            services.Go.Client(gone);
        });
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

    private int mailRead; // counts up per read, so an older read finishing late is dropped

    /// <summary>
    /// The latest mail with this client, read off the screen's thread: finding it means unlocking cached mail
    /// one message at a time, which mustn't hold up typing or saving.
    /// </summary>
    private async Task LoadMail()
    {
        var matcher = new ClientMail(client);
        if (matcher.IsEmpty)
            return;
        var read = ++mailRead;
        var accounts = services.Store.ListMailAccounts();
        var found = await Task.Run(() => services.Store.FindMail(accounts.Select(a => a.Id), matcher.Matches, limit: 12));
        if (read != mailRead)
            return;
        Mail.Clear();
        foreach (var mail in found)
            Mail.Add(new ClientMailRow(mail, accounts.First(a => a.Id == mail.AccountId), services.Store));
        OnPropertyChanged(nameof(HasMail));
    }

    public void Refresh()
    {
        LoadOnboarding();
        LoadRecords();
        LoadProjects();
        Documents.Clear();
        Tasks.Clear();
        Events.Clear();
        Mail.Clear();
        if (!isNew)
        {
            LoadMail().Forget("Reading the client's mail");
            var summary = ClientDirectory.Summarise(client, services.Store.List(), services.Store.ListTasks(),
                services.Store.ListEvents(Today, Today.AddDays(90)), Today);
            foreach (var doc in summary.Documents)
                Documents.Add(new DocumentRow(doc, Today));
            foreach (var task in summary.OpenTasks)
                Tasks.Add(new ClientTaskViewModel(task, this));
            foreach (var ev in summary.UpcomingEvents)
                Events.Add(new ClientEventViewModel(ev));
            Owed = summary.Owed == 0 ? "" : $"{Money.Rand(summary.Owed)} invoiced and not yet marked paid";
        }
        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(HasEvents));
        OnPropertyChanged(nameof(HasMail));
        OnPropertyChanged(nameof(HasMailboxes));
        OnPropertyChanged(nameof(CanWriteMail));
        OnPropertyChanged(nameof(MailEmptyText));
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

/// <summary>A mail with the client, shown on their page.</summary>
public sealed class ClientMailRow(MailSummary summary, MailAccount account, Webignite.Data.LocalStore store)
{
    public MailSummary Summary => summary;
    public string Subject => string.IsNullOrWhiteSpace(summary.Subject) ? "(no subject)" : summary.Subject;
    public bool IsUnread => !summary.Seen;

    public string Detail
    {
        get
        {
            var folder = store.ListMailFolders(account.Id).FirstOrDefault(f => f.Path == summary.Folder);
            var outgoing = MailFolders.IsOutgoing(folder?.Role);
            var who = outgoing ? "To " + string.Join(", ", summary.To.Select(p => p.Display)) : summary.From?.Display ?? "";
            return $"{who}  ·  {folder?.Name ?? summary.Folder}, {account.Address.Split('@')[0]}@";
        }
    }

    public string When => summary.Date.ToLocalTime().ToString(summary.Date.Year == DateTime.Today.Year ? "d MMM" : "d MMM yyyy");
}
