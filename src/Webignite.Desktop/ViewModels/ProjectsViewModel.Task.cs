using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Mail;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// The open task, in the third pane: its notes, subtasks, who it's for, dates, heading, and what it's about.
public sealed partial class ProjectsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTask))]
    public partial TaskDetail? Detail { get; private set; }

    public bool HasTask => Detail is not null;

    [RelayCommand]
    private void OpenTask(TaskRow row) => OpenTaskById(row.Task.Id);

    /// <summary>Opens a task by id, from the list or (later) My tasks.</summary>
    public void OpenTaskById(string id)
    {
        SaveTyped();
        if (services.Projects.GetTask(id) is not { } task)
            return;
        // A subtask opens its task, where it's listed.
        if (task.ParentId is { } parentId)
        {
            if (services.Projects.GetTask(parentId) is not { } parent)
                return;
            (task, id) = (parent, parentId);
        }
        // In My tasks the task opens beside the list; otherwise beside its project.
        if (!IsMyTasks && open?.Id != task.ProjectId && services.Projects.Get(task.ProjectId) is { } project)
            Show(project);
        Detail = new TaskDetail(task, this, services);
        foreach (var row in Rows.OfType<TaskRow>().Concat(MyRows.OfType<TaskRow>()))
            row.IsOpen = row.Task.Id == id;
        if (!Panels["task"].IsShown)
            Panels["task"].IsShown = true;
        OnPropertyChanged(nameof(HoldsWork));
    }

    [RelayCommand]
    private void CloseTask()
    {
        SaveTyped();
        Detail = null;
        foreach (var row in Rows.OfType<TaskRow>().Concat(MyRows.OfType<TaskRow>()))
            row.IsOpen = false;
        if (Panels.CanHide("task"))
            Panels["task"].IsShown = false;
    }

    /// <summary>A change made in the task pane: saved, then shown in the list too.</summary>
    internal void ChangeOpenTask(Func<ProjectTask, ProjectTask> change, bool typed = false)
    {
        if (Detail is not { } detail)
            return;
        var (saved, problems) = Ours(() => services.Projects.ChangeTask(detail.Task.Id, change));
        if (saved is null)
        {
            detail.Problem = string.Join(" ", problems);
            detail.Reload();
            return;
        }
        var tagsBefore = detail.Task.Tags;
        detail.Problem = "";
        detail.Saved(saved, typed);
        // Typing in the title only renames the row; new tags change the filter's list, so everything is shown again.
        if (typed && saved.Tags.SequenceEqual(tagsBefore) && Rows.OfType<TaskRow>().Concat(MyRows.OfType<TaskRow>()).FirstOrDefault(r => r.Task.Id == saved.Id) is { } row)
        {
            row.Task = saved;
            row.Reset();
        }
        else
        {
            ShowTasks();
        }
    }

    internal void AddSubtask(string title)
    {
        if (Detail is not { } detail || title.Trim().Length == 0)
            return;
        var parent = detail.Task;
        var (saved, problems) = Ours(() => services.Projects.AddTask(new ProjectTask
        {
            Id = ProjectRules.NewId(), ProjectId = parent.ProjectId, HeadingId = parent.HeadingId, ParentId = parent.Id, Title = title,
        }));
        detail.Problem = saved is null ? string.Join(" ", problems) : "";
        if (saved is null)
            return;
        ShowTasks();
        detail.LoadSubtasks();
    }

    /// <summary>Finds mail to link, by words in its subject, sender or text. Read off the screen's thread.</summary>
    internal async Task<IReadOnlyList<MailSummary>> FindMailAsync(string words)
    {
        var accounts = services.Store.ListMailAccounts().Select(a => a.Id).ToList();
        if (accounts.Count == 0 || words.Trim().Length < 2)
            return [];
        return await Task.Run(() => services.Store.SearchMail(accounts, words.Trim(), limit: 8));
    }

    internal void OpenMail(TaskMail link)
    {
        var accounts = services.Store.ListMailAccounts().Select(a => a.Id).ToList();
        // Found again by its message id when it has moved; else where it was.
        var found = link.MessageId is { } id ? services.Store.FindMail(accounts, m => m.MessageId == id, limit: 1).FirstOrDefault() : null;
        services.Go.Mail(found ?? new MailSummary { AccountId = link.AccountId, Folder = link.Folder, Uid = link.Uid, MessageId = link.MessageId, Subject = link.Subject, Date = link.Date });
    }

    internal void OpenClient(Client client) => services.Go.Client(client);

    internal void OpenDocument(StoredDocument stored) => services.Go.Document(stored);

    /// <summary>The headings a task can go under in its project, for the task pane.</summary>
    internal IReadOnlyList<Choice<string?>> HeadingChoices(string projectId) =>
        [new Choice<string?>("Not under a heading", null), .. services.Projects.Headings(projectId).Select(h => new Choice<string?>(h.Name, h.Id))];

    /// <summary>Starts the Time page's timer on a task, with its client; one timer runs at a time.</summary>
    internal void StartTimer(ProjectTask task)
    {
        SaveTyped();
        if (services.Timer.Running is { } running)
        {
            services.ShowNote?.Invoke("A timer is running already", $"Stop \"{running.Note}\" on the Time page first.", () => services.Go.To(Page.Time));
            return;
        }
        var project = services.Projects.Get(task.ProjectId);
        services.Timer.Start(new Webignite.Core.Time.RunningTimer
        {
            Started = DateTimeOffset.Now,
            Note = ProjectRules.TimerNote(task, project?.Name),
            ClientId = task.ClientId ?? project?.ClientId,
        });
        services.ShowNote?.Invoke("Timer started", ProjectRules.TimerNote(task, project?.Name), () => services.Go.To(Page.Time));
    }
}

/// <summary>The task open in the third pane. Changes go through the page, which keeps the list in step.</summary>
public sealed partial class TaskDetail : ObservableObject
{
    private readonly ProjectsViewModel page;
    private readonly AppServices services;
    private bool loading;
    private int searching;

    public TaskDetail(ProjectTask task, ProjectsViewModel page, AppServices services)
    {
        this.page = page;
        this.services = services;
        Task = task;
        People = page.People;
        Clients = page.ClientChoices;
        Reload();
    }

    public ProjectTask Task { get; private set; }

    public IReadOnlyList<Choice<string?>> People { get; }
    public IReadOnlyList<Choice<Client?>> Clients { get; }
    public IReadOnlyList<Choice<string?>> Headings { get; private set; } = [];
    public IReadOnlyList<Choice<StoredDocument?>> Documents { get; private set; } = [];

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial bool Done { get; set; }
    [ObservableProperty] public partial string Notes { get; set; } = "";
    [ObservableProperty] public partial Choice<string?>? AssignedTo { get; set; }
    [ObservableProperty] public partial DateTime? Start { get; set; }
    [ObservableProperty] public partial DateTime? Due { get; set; }
    [ObservableProperty] public partial Choice<string?>? Heading { get; private set; }
    [ObservableProperty] public partial Choice<Client?>? Client { get; private set; }
    [ObservableProperty] public partial Choice<StoredDocument?>? Document { get; private set; }
    [ObservableProperty] public partial string History { get; private set; } = "";
    [ObservableProperty] public partial string Problem { get; set; } = "";
    [ObservableProperty] public partial string NewSubtask { get; set; } = "";
    [ObservableProperty] public partial string MailSearch { get; set; } = "";
    [ObservableProperty] public partial Choice<TaskPriority>? Priority { get; set; }
    [ObservableProperty] public partial Choice<TaskRepeat>? Repeat { get; set; }

    public IReadOnlyList<Choice<TaskRepeat>> Repeats => ProjectsViewModel.Repeats;
    [ObservableProperty] public partial string TagsText { get; set; } = "";

    public IReadOnlyList<Choice<TaskPriority>> Priorities => ProjectsViewModel.Priorities;

    /// <summary>Tags used elsewhere in the project and not on this task yet, to add with a click.</summary>
    public IReadOnlyList<string> OtherTags { get; private set; } = [];
    public bool HasOtherTags => OtherTags.Count > 0;

    public ObservableCollection<TaskRow> Subtasks { get; } = [];
    public ObservableCollection<MailHit> MailFound { get; } = [];

    public bool HasProblem => Problem.Length > 0;
    public bool HasMail => Task.Mail is not null;
    public bool NoMail => Task.Mail is null;
    public string MailSubject => Task.Mail is { } mail ? (mail.Subject.Length > 0 ? mail.Subject : "(no subject)") : "";
    public string MailAbout => Task.Mail is { } mail ? string.Join(" · ", new[] { mail.From, mail.Date.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) }.Where(t => !string.IsNullOrEmpty(t))) : "";
    public bool HasClient => Client?.Value is not null;
    public bool HasDocument => Document?.Value is not null;
    public bool HasMailFound => MailFound.Count > 0;

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    /// <summary>Shows the task as stored now, with its subtasks.</summary>
    public void Reload()
    {
        if (services.Projects.GetTask(Task.Id) is { } fresh)
            Task = fresh;
        loading = true;
        Title = Task.Title;
        Done = Task.Done;
        Notes = Task.Notes;
        AssignedTo = People.FirstOrDefault(p => p.Value == Task.AssignedTo) ?? People.FirstOrDefault();
        Start = Task.Start?.ToDateTime(TimeOnly.MinValue);
        Due = Task.Due?.ToDateTime(TimeOnly.MinValue);
        Priority = ProjectsViewModel.Priorities.First(p => p.Value == Task.Priority);
        Repeat = ProjectsViewModel.Repeats.First(r => r.Value == Task.Repeat);
        TagsText = string.Join(", ", Task.Tags);
        ShowOtherTags();
        Headings = page.HeadingChoices(Task.ProjectId);
        OnPropertyChanged(nameof(Headings));
        Heading = Headings.FirstOrDefault(h => h.Value == Task.HeadingId) ?? Headings.FirstOrDefault();
        OnPropertyChanged(nameof(Heading));
        Client = Clients.FirstOrDefault(c => c.Value?.Id == Task.ClientId) ?? Clients.FirstOrDefault();
        LoadDocuments();
        loading = false;
        ShowLinksAndHistory();
        LoadSubtasks();
    }

    /// <summary>After a save: the stamps and links shown again; what's being typed is left as it is.</summary>
    internal void Saved(ProjectTask saved, bool typed)
    {
        Task = saved;
        if (!typed)
        {
            Reload();
            return;
        }
        ShowLinksAndHistory();
        ShowOtherTags();
    }

    private void ShowLinksAndHistory()
    {
        var team = services.Team();
        var names = new Dictionary<string, string> { [team.Me!.Id] = team.Me.Name, [team.Partner!.Id] = team.Partner.Name };
        History = ProjectRules.History(Task, names, TimeZoneInfo.Local, CultureInfo.CurrentCulture);
        OnPropertyChanged(nameof(HasMail));
        OnPropertyChanged(nameof(NoMail));
        OnPropertyChanged(nameof(MailSubject));
        OnPropertyChanged(nameof(MailAbout));
        OnPropertyChanged(nameof(HasClient));
        OnPropertyChanged(nameof(HasDocument));
    }

    // The quotes and invoices of the task's client, or else the project's.
    private void LoadDocuments()
    {
        var clientId = Task.ClientId ?? services.Projects.Get(Task.ProjectId)?.ClientId;
        var client = clientId is null ? null : services.Clients.Get(clientId);
        Documents = [new Choice<StoredDocument?>("None", null), .. ProjectRules.DocumentsFor(client, services.Documents.List())
            .Select(d => new Choice<StoredDocument?>(ProjectRules.DocumentLabel(d), d))];
        OnPropertyChanged(nameof(Documents));
        Document = Documents.FirstOrDefault(d => d.Value?.Id == Task.DocumentId) ?? Documents[0];
        // The list was rebuilt, so the drop-down needs its pick again.
        OnPropertyChanged(nameof(Document));
    }

    internal void LoadSubtasks()
    {
        Subtasks.Clear();
        foreach (var sub in services.Projects.AllTasks(Task.ProjectId).Where(t => t.ParentId == Task.Id))
            Subtasks.Add(new TaskRow(sub, null, "", People, page));
    }

    partial void OnTitleChanged(string value)
    {
        if (!loading)
            page.Typed(this);
    }

    partial void OnNotesChanged(string value)
    {
        if (!loading)
            page.Typed(this);
    }

    partial void OnTagsTextChanged(string value)
    {
        if (!loading)
            page.Typed(this);
    }

    partial void OnRepeatChanged(Choice<TaskRepeat>? value)
    {
        if (value is { } picked)
            Change(t => t with { Repeat = picked.Value });
    }

    partial void OnPriorityChanged(Choice<TaskPriority>? value)
    {
        if (value is { } picked)
            Change(t => t with { Priority = picked.Value });
    }

    private void ShowOtherTags()
    {
        var mine = ProjectRules.Tags(TagsText, page.ProjectTags);
        OtherTags = [.. page.ProjectTags.Where(t => !mine.Contains(t, StringComparer.OrdinalIgnoreCase))];
        OnPropertyChanged(nameof(OtherTags));
        OnPropertyChanged(nameof(HasOtherTags));
    }

    [RelayCommand]
    private void AddTag(string tag)
    {
        TagsText = string.Join(", ", ProjectRules.Tags(TagsText + "," + tag, page.ProjectTags));
        page.Keep();
    }

    /// <summary>Saves the title and notes typed, a moment after typing stops.</summary>
    internal void SaveTyped()
    {
        if (Title.Trim().Length == 0)
        {
            Problem = "Give the task a title.";
            return;
        }
        var (title, notes) = (Title, Notes);
        var tags = ProjectRules.Tags(TagsText, page.ProjectTags);
        page.ChangeOpenTask(t => t with { Title = title, Notes = notes.TrimEnd(), Tags = tags }, typed: true);
    }

    partial void OnDoneChanged(bool value) => Change(t => t with { Done = value });
    partial void OnAssignedToChanged(Choice<string?>? value) => Change(t => t with { AssignedTo = value?.Value });
    partial void OnStartChanged(DateTime? value) => Change(t => t with { Start = value is { } d ? DateOnly.FromDateTime(d) : null });
    partial void OnDueChanged(DateTime? value) => Change(t => t with { Due = value is { } d ? DateOnly.FromDateTime(d) : null });

    private void Change(Func<ProjectTask, ProjectTask> change)
    {
        if (!loading)
            page.ChangeOpenTask(change);
    }

    // The lists below are one-way, so only a real pick counts.

    public void PickHeading(Choice<string?> choice)
    {
        Heading = choice;
        Change(t => t with { HeadingId = choice.Value });
    }

    public void PickClient(Choice<Client?> choice)
    {
        Client = choice;
        Change(t => t with { ClientId = choice.Value?.Id });
    }

    public void PickDocument(Choice<StoredDocument?> choice)
    {
        Document = choice;
        Change(t => t with { DocumentId = choice.Value?.Id });
    }

    [RelayCommand]
    private void AddSubtask()
    {
        var title = NewSubtask;
        NewSubtask = "";
        page.AddSubtask(title);
    }

    [RelayCommand]
    private void Close() => page.CloseTaskCommand.Execute(null);

    [RelayCommand]
    private void StartTimer() => page.StartTimer(Task);

    [RelayCommand]
    private void Delete()
    {
        if (page.Rows.OfType<TaskRow>().Concat(page.MyRows.OfType<TaskRow>()).FirstOrDefault(r => r.Task.Id == Task.Id) is { } row)
            page.DeleteTask(row);
    }

    [RelayCommand]
    private void OpenClient()
    {
        if (Client?.Value is { } client)
            page.OpenClient(client);
    }

    [RelayCommand]
    private void OpenDocument()
    {
        if (Document?.Value is { } stored)
            page.OpenDocument(stored);
    }

    // Mail

    partial void OnMailSearchChanged(string value) => Search(value).Forget("Finding mail to link");

    /// <summary>The search's finish, for the screenshot tool to wait on.</summary>
    public Task Searched { get; private set; } = System.Threading.Tasks.Task.CompletedTask;

    private Task Search(string words) => Searched = SearchAsync(words, ++searching);

    private async Task SearchAsync(string words, int run)
    {
        var found = await page.FindMailAsync(words);
        if (run != searching)
            return;
        MailFound.Clear();
        foreach (var mail in found)
            MailFound.Add(new MailHit(mail, this));
        OnPropertyChanged(nameof(HasMailFound));
    }

    internal void LinkMail(MailSummary mail)
    {
        MailSearch = "";
        MailFound.Clear();
        OnPropertyChanged(nameof(HasMailFound));
        page.ChangeOpenTask(t => t with { Mail = TaskMail.Of(mail) });
    }

    [RelayCommand]
    private void OpenMail()
    {
        if (Task.Mail is { } link)
            page.OpenMail(link);
    }

    [RelayCommand]
    private void RemoveMail() => page.ChangeOpenTask(t => t with { Mail = null });
}

/// <summary>A mail found to link to a task.</summary>
public sealed partial class MailHit(MailSummary mail, TaskDetail task)
{
    public MailSummary Mail { get; } = mail;
    public string Subject => Mail.Subject.Length > 0 ? Mail.Subject : "(no subject)";
    public string About => string.Join(" · ", new[] { Mail.From?.Display, Mail.Date.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) }.Where(t => !string.IsNullOrEmpty(t)));

    [RelayCommand]
    private void Link() => task.LinkMail(Mail);
}
