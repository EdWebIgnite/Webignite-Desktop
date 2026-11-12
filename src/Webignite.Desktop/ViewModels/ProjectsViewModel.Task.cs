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
    internal bool loading;
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
    /// <summary>The description as plain text: what it says, for checks. The editor works on <see cref="Description"/>.</summary>
    public string Notes
    {
        get => Webignite.Core.Writing.RichText.ToText(Description).TrimEnd();
        set
        {
            Description = Webignite.Core.Writing.RichText.FromText(value);
            DescriptionRevision++;
        }
    }

    /// <summary>The description with its formatting, as Asana's: bold, lists, links. Saved a moment after typing stops.</summary>
    [ObservableProperty] public partial Webignite.Core.Writing.RichDocument Description { get; set; } = Webignite.Core.Writing.RichDocument.Empty();

    [ObservableProperty] public partial int DescriptionRevision { get; set; }

    /// <summary>"0/1" by the Subtasks heading, as Asana's.</summary>
    [ObservableProperty] public partial string SubtaskTally { get; private set; } = "";

    /// <summary>Who it's for and what project and heading it's in, for the fields at the top.</summary>
    public string ProjectName => page.OpenProjectName;

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

    // How it repeats beyond every one: every so many, and for a monthly one by the day of the month or by its
    // weekday ("the 1st Monday"), which is read from the due date so the two can never disagree.

    /// <summary>1 to 12: every week, every 2 weeks, every 3 months.</summary>
    public IReadOnlyList<int> RepeatCounts { get; } = [.. Enumerable.Range(1, ProjectRules.MostRepeatEvery)];

    [ObservableProperty] public partial int RepeatEvery { get; set; } = 1;
    [ObservableProperty] public partial bool RepeatByWeekday { get; set; }

    public bool HasDue => Due is not null;
    public bool IsRepeating => Repeat is { Value: not TaskRepeat.None };
    public bool IsMonthlyRepeat => Repeat is { Value: TaskRepeat.Monthly } && Due is not null;
    public bool RepeatByDay => !RepeatByWeekday;

    /// <summary>"days", "weeks", "months" or "years", after the count.</summary>
    public string RepeatUnit => (Repeat?.Value ?? TaskRepeat.None) switch
    {
        TaskRepeat.Daily => RepeatEvery == 1 ? "day" : "days",
        TaskRepeat.Weekly => RepeatEvery == 1 ? "week" : "weeks",
        TaskRepeat.Monthly => RepeatEvery == 1 ? "month" : "months",
        _ => RepeatEvery == 1 ? "year" : "years",
    };

    public string RepeatDayLabel => Due is { } due ? $"On day {due.Day}" : "On the same day";

    public string RepeatWeekdayLabel => Due is { } due && ProjectRules.WeekdayOf(DateOnly.FromDateTime(due)) is var on
        ? $"On the {ProjectRules.NthWord(on.Nth)} {on.Day}"
        : "On the same weekday";

    /// <summary>How it repeats, in words, under the choices: "Every 2 months on the 1st Monday".</summary>
    public string RepeatWords => ProjectRules.Label(Task);

    /// <summary>The time it is due by, as typed: "14:00". Empty for any time that day.</summary>
    [ObservableProperty] public partial string DueTimeText { get; set; } = "";
    [ObservableProperty] public partial string DueTimeProblem { get; private set; } = "";

    /// <summary>After the date in the due box: its time, and how it repeats.</summary>
    public string DueNote => string.Join(", ", new[]
    {
        Task.DueTime is { } time ? time.ToString("t", System.Globalization.CultureInfo.CurrentCulture) : "",
        Task.Repeat == TaskRepeat.None ? "" : ProjectRules.Label(Task).ToLowerInvariant(),
    }.Where(part => part.Length > 0));

    private void ShowRepeat()
    {
        foreach (var name in new[] { nameof(HasDue), nameof(IsRepeating), nameof(IsMonthlyRepeat), nameof(RepeatByDay), nameof(RepeatUnit), nameof(RepeatDayLabel), nameof(RepeatWeekdayLabel), nameof(RepeatWords), nameof(DueNote) })
            OnPropertyChanged(name);
    }

    partial void OnRepeatEveryChanged(int value)
    {
        Change(t => t with { RepeatEvery = ProjectRules.Every(value) });
        ShowRepeat();
    }

    partial void OnRepeatByWeekdayChanged(bool value)
    {
        Change(t => t with { RepeatOn = value && t.Due is { } due ? ProjectRules.WeekdayOf(due) : null });
        ShowRepeat();
    }

    [RelayCommand]
    private void RepeatOnDay() => RepeatByWeekday = false;

    [RelayCommand]
    private void RepeatOnWeekday() => RepeatByWeekday = true;

    partial void OnDueTimeTextChanged(string value)
    {
        if (loading)
            return;
        var text = value.Trim();
        if (text.Length == 0)
        {
            DueTimeProblem = "";
            Change(t => t with { DueTime = null });
        }
        else if (TimeOnly.TryParse(text, System.Globalization.CultureInfo.CurrentCulture, out var time)
                 || TimeOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out time))
        {
            DueTimeProblem = "";
            Change(t => t with { DueTime = time });
        }
        else
        {
            // Left as typed: half a time ("14:") is on its way to being one.
            DueTimeProblem = "A time like 14:00.";
            return;
        }
        ShowRepeat();
    }
    [ObservableProperty] public partial string TagsText { get; set; } = "";

    public IReadOnlyList<Choice<TaskPriority>> Priorities => ProjectsViewModel.Priorities;

    /// <summary>Tags used elsewhere in the project and not on this task yet, to add with a click.</summary>
    public IReadOnlyList<string> OtherTags { get; private set; } = [];
    public bool HasOtherTags => OtherTags.Count > 0;

    public ObservableCollection<TaskRow> Subtasks { get; } = [];

    /// <summary>Files attached to the task, as tiles, pictures shown small.</summary>
    public ObservableCollection<AttachmentRow> Attachments { get; } = [];

    public bool HasAttachments => Attachments.Count > 0;
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
        (titleTyped, descriptionTyped, tagsTyped) = (false, false, false);
        Title = Task.Title;
        Done = Task.Done;
        Description = ProjectRules.Description(Task);
        DescriptionRevision = 0;
        AssignedTo = People.FirstOrDefault(p => p.Value == Task.AssignedTo) ?? People.FirstOrDefault();
        Start = Task.Start?.ToDateTime(TimeOnly.MinValue);
        Due = Task.Due?.ToDateTime(TimeOnly.MinValue);
        Priority = ProjectsViewModel.Priorities.First(p => p.Value == Task.Priority);
        Repeat = ProjectsViewModel.Repeats.First(r => r.Value == Task.Repeat);
        RepeatEvery = ProjectRules.Every(Task.RepeatEvery);
        RepeatByWeekday = Task.RepeatOn is not null;
        DueTimeText = Task.DueTime is { } time ? time.ToString("t", System.Globalization.CultureInfo.CurrentCulture) : "";
        DueTimeProblem = "";
        TagsText = string.Join(", ", Task.Tags);
        ShowOtherTags();
        Headings = page.HeadingChoices(Task.ProjectId);
        OnPropertyChanged(nameof(Headings));
        Heading = Headings.FirstOrDefault(h => h.Value == Task.HeadingId) ?? Headings.FirstOrDefault();
        OnPropertyChanged(nameof(Heading));
        Client = Clients.FirstOrDefault(c => c.Value?.Id == Task.ClientId) ?? Clients.FirstOrDefault();
        LoadDocuments();
        loading = false;
        ShowRepeat();
        ShowLinksAndHistory();
        LoadSubtasks();
        LoadDependencies();
        LoadFieldEditors();
        Attachments.Clear();
        foreach (var attachment in Task.Attachments)
            Attachments.Add(new AttachmentRow(Task, attachment, page, 120));
        OnPropertyChanged(nameof(HasAttachments));
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
        var names = new Dictionary<string, string>
        {
            [team.Me!.Id] = team.Me.Name,
            [team.Partner!.Id] = team.Partner.Name,
            [Actors.Claude] = Actors.Name(Actors.Claude)!,
            [Actors.ChatGpt] = Actors.Name(Actors.ChatGpt)!,
        };
        History = ProjectRules.History(Task, names, TimeZoneInfo.Local, CultureInfo.CurrentCulture);
        ShowLikesAndFollowers();
        LoadFeed();
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
        var subs = services.Projects.AllTasks(Task.ProjectId).Where(t => t.ParentId == Task.Id).ToList();
        foreach (var sub in subs)
            Subtasks.Add(new TaskRow(sub, null, "", People, page));
        SubtaskTally = ProjectRules.SubtaskTally(subs);
        OnPropertyChanged(nameof(HasSubtasks));
        OnPropertyChanged(nameof(ShowSubtaskBox));
    }

    public bool HasSubtasks => Subtasks.Count > 0;

    /// <summary>The "2/3" by Subtasks worked out again, after one is ticked off or on.</summary>
    internal void CountSubtasks() => SubtaskTally = ProjectRules.SubtaskTally(Subtasks.Select(r => r.Task));

    /// <summary>The box to type a subtask: shown once there are some, or after + is clicked.</summary>
    public bool ShowSubtaskBox => HasSubtasks || AddingSubtask;

    public bool HasAssignee => AssignedTo?.Value is not null;

    partial void OnAddingSubtaskChanged(bool value) => OnPropertyChanged(nameof(ShowSubtaskBox));

    /// <summary>The subtask box is shown: always once there are subtasks, or after + is clicked.</summary>
    [ObservableProperty] public partial bool AddingSubtask { get; set; }

    /// <summary>The + by Subtasks, as Asana's: shows the box to type the first (or next) subtask.</summary>
    [RelayCommand]
    private void StartSubtask() => AddingSubtask = true;

    // Which of the typed fields changed since the last save, so saving writes only those and leaves the rest as stored
    // (a description changed meanwhile by Claude or another tab isn't put back).
    private bool titleTyped, descriptionTyped, tagsTyped;

    /// <summary>Whether something typed here isn't saved yet.</summary>
    internal bool IsTyping => titleTyped || descriptionTyped || tagsTyped;

    partial void OnTitleChanged(string value)
    {
        if (loading)
            return;
        titleTyped = true;
        page.Typed(this);
    }

    partial void OnDescriptionRevisionChanged(int value)
    {
        if (loading || value <= 0)
            return;
        descriptionTyped = true;
        page.Typed(this);
    }

    partial void OnTagsTextChanged(string value)
    {
        if (loading)
            return;
        tagsTyped = true;
        page.Typed(this);
    }

    partial void OnRepeatChanged(Choice<TaskRepeat>? value)
    {
        if (value is { } picked)
            Change(t => t with { Repeat = picked.Value });
        ShowRepeat();
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
        var (title, description) = (Title, Description.Clone());
        var tags = ProjectRules.Tags(TagsText, page.ProjectTags);
        var (withTitle, withDescription, withTags) = (titleTyped, descriptionTyped, tagsTyped);
        (titleTyped, descriptionTyped, tagsTyped) = (false, false, false);
        page.ChangeOpenTask(t =>
        {
            var changed = withDescription ? ProjectRules.WithDescription(t, description) : t;
            return changed with { Title = withTitle ? title : t.Title, Tags = withTags ? tags : t.Tags };
        }, typed: true);
    }

    partial void OnDoneChanged(bool value)
    {
        Change(t => t with { Done = value });
        if (value && !loading && TaskDependencies.WaitingOn(Task, services.Projects.AllTasks(Task.ProjectId)) is { Length: > 0 } waiting)
            page.Tell($"Done, though it was still waiting on {waiting}.");
    }
    partial void OnAssignedToChanged(Choice<string?>? value)
    {
        OnPropertyChanged(nameof(HasAssignee));
        Change(t => t with { AssignedTo = value?.Value });
    }
    partial void OnStartChanged(DateTime? value) => Change(t => t with { Start = value is { } d ? DateOnly.FromDateTime(d) : null });
    partial void OnDueChanged(DateTime? value)
    {
        // A monthly repeat that goes by weekday follows the due date: moved to a Tuesday, it repeats on that Tuesday.
        Change(t =>
        {
            var due = value is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
            return t with { Due = due, RepeatOn = t.RepeatOn is not null && due is { } on ? ProjectRules.WeekdayOf(on) : null, DueTime = due is null ? null : t.DueTime };
        });
        ShowRepeat();
    }

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
