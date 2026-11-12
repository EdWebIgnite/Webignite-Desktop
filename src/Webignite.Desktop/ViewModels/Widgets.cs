using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Calendar;
using Webignite.Core.Clients;
using Webignite.Core.Layout;
using Webignite.Core.Projects;
using Webignite.Core.Time;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// What a widget shows. Each kind of widget has its own; it reads what it needs when built and when the page comes
/// back on screen, and lets go of anything it follows when the widget or the page closes.
/// </summary>
public abstract partial class WidgetBody : ObservableObject, IDisposable
{
    /// <summary>The widget's heading; null uses the name of its kind.</summary>
    public virtual string? Title => null;

    /// <summary>Reads again; the page calls it when it comes back on screen.</summary>
    public virtual void Refresh()
    {
    }

    /// <summary>Saves anything typed and not yet kept.</summary>
    public virtual void Keep()
    {
    }

    public virtual void Dispose() => GC.SuppressFinalize(this);

    public static WidgetBody For(Widget widget, AppServices services, MyPagesViewModel page) => widget.Kind switch
    {
        WidgetKind.MyTasks => new TasksBody(services, null),
        WidgetKind.Project => new TasksBody(services, widget.Target),
        WidgetKind.Today => new TodayBody(services),
        WidgetKind.Month => new MonthBody(services),
        WidgetKind.Note => new NoteBody(widget, page),
        WidgetKind.Calculator => new CalculatorBody(page.Tools),
        WidgetKind.Clipboard => new ClipboardBody(page.Tools),
        WidgetKind.Colours => new ColoursBody(page.Tools),
        WidgetKind.Timer => new TimerBody(services),
        WidgetKind.Client => new ClientBody(services, widget.Target),
        WidgetKind.NeedsYou => new NeedsYouBody(services),
        WidgetKind.Mail => new MailBody(services),
        WidgetKind.ClientMail => new MailBody(services, widget.Target),
        WidgetKind.Files => new FilesBody(widget, services, page),
        _ => new SoonBody(),
    };
}

/// <summary>A kind this version doesn't know (from a newer one); kept on the page so nothing is lost.</summary>
public sealed class SoonBody : WidgetBody;

/// <summary>A line in a list of tasks: tick it off, or click it to open it in Projects.</summary>
public sealed partial class TaskLine : ObservableObject
{
    private readonly AppServices services;
    private bool loading = true;

    public TaskLine(ProjectTask task, string about, bool overdue, AppServices services)
    {
        this.services = services;
        Task = task;
        About = about;
        Overdue = overdue;
        Done = task.Done;
        loading = false;
    }

    public ProjectTask Task { get; }
    public string Title => Task.Title;
    public string About { get; }
    public bool HasAbout => About.Length > 0;
    public bool Overdue { get; }

    [ObservableProperty] public partial bool Done { get; set; }

    partial void OnDoneChanged(bool value)
    {
        if (!loading)
            services.Projects.ChangeTask(Task.Id, t => t with { Done = value });
    }

    [RelayCommand]
    private void Open() => services.Go.ProjectTask(Task.Id);
}

/// <summary>A heading inside a project's widget.</summary>
public sealed record TaskHeadingLine(string Name);

/// <summary>My tasks across projects by when they're due, or one project's open tasks under their headings.</summary>
public sealed partial class TasksBody : WidgetBody
{
    private readonly AppServices services;
    private readonly string? projectId;

    public TasksBody(AppServices services, string? projectId)
    {
        this.services = services;
        this.projectId = projectId;
        services.Projects.Changed += ProjectsChanged;
        Refresh();
    }

    public ObservableCollection<object> Lines { get; } = [];

    [ObservableProperty] public partial string Heading { get; private set; } = "";
    [ObservableProperty] public partial string Empty { get; private set; } = "";

    public override string? Title => projectId is null ? null : Heading;

    private void ProjectsChanged(string? id)
    {
        if (projectId is null || id is null || id == projectId)
            Refresh();
    }

    public override void Refresh()
    {
        Lines.Clear();
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (projectId is null)
            ShowMine(today);
        else
            ShowProject(today);
        OnPropertyChanged(nameof(Title));
    }

    private void ShowMine(DateOnly today)
    {
        var names = services.Projects.Projects().ToDictionary(p => p.Id, p => p.Name);
        foreach (var (group, tasks) in ProjectRules.ByWhen(services.Projects.Mine(services.Me().Id), today, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek))
        {
            Lines.Add(new TaskHeadingLine(group));
            foreach (var task in tasks)
                Lines.Add(new TaskLine(task, names.GetValueOrDefault(task.ProjectId) ?? "", group == "Overdue", services));
        }
        Empty = Lines.Count == 0 ? "Nothing given to you right now." : "";
    }

    private void ShowProject(DateOnly today)
    {
        if (services.Projects.Get(projectId!) is not { } project)
        {
            Heading = "A project";
            Empty = "This project was deleted. Remove the widget, or pick another.";
            return;
        }
        Heading = project.Name;
        var tasks = services.Projects.AllTasks(project.Id).Where(t => t.ParentId is null).ToList();
        foreach (var (heading, list) in ProjectRules.Sections(services.Projects.Headings(project.Id), tasks))
        {
            var open = list.Where(t => !t.Done).ToList();
            if (open.Count == 0)
                continue;
            if (heading is not null)
                Lines.Add(new TaskHeadingLine(heading.Name));
            foreach (var task in open)
                Lines.Add(new TaskLine(task, task.Due is { } due ? due.ToString("d MMM", CultureInfo.CurrentCulture) : "", task.Due < today, services));
        }
        var done = tasks.Count(t => t.Done);
        Empty = Lines.Count > 0 ? "" : tasks.Count == 0 ? "No tasks yet." : $"All {done} done.";
    }

    public override void Dispose()
    {
        services.Projects.Changed -= ProjectsChanged;
        base.Dispose();
    }
}

/// <summary>What's booked and due today, like Overview's Today.</summary>
public sealed partial class TodayBody : WidgetBody
{
    private readonly AppServices services;

    public TodayBody(AppServices services)
    {
        this.services = services;
        Refresh();
    }

    public ObservableCollection<AgendaItemViewModel> Items { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; private set; }

    public override void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        Items.Clear();
        foreach (var item in Agenda.Build(today, today, services.Store.ListEvents(today, today), services.Store.ListTasks(), services.Documents.List(),
                     services.Projects.Agenda(today, today, services.Me().Id)).Where(i => !i.Done))
            Items.Add(new AgendaItemViewModel(item));
        IsEmpty = Items.Count == 0;
    }
}

/// <summary>A day in the month widget: its number, and a dot for each thing on it (up to three).</summary>
public sealed record MonthDay(DateOnly Date, bool IsToday, bool IsOtherMonth, int Count)
{
    public string Number => Date.Day.ToString(CultureInfo.CurrentCulture);
    public bool HasItems => Count > 0;
    public string Tip => Count switch
    {
        0 => Date.ToString("dddd d MMMM", CultureInfo.CurrentCulture),
        1 => Date.ToString("dddd d MMMM", CultureInfo.CurrentCulture) + ": 1 thing",
        _ => Date.ToString("dddd d MMMM", CultureInfo.CurrentCulture) + $": {Count} things",
    };
}

/// <summary>This month at a glance: each day with a dot where something is on. A click opens Calendar.</summary>
public sealed partial class MonthBody : WidgetBody
{
    private readonly AppServices services;

    public MonthBody(AppServices services)
    {
        this.services = services;
        Refresh();
    }

    public ObservableCollection<MonthDay> Days { get; } = [];

    [ObservableProperty] public partial string MonthTitle { get; private set; } = "";

    public override string? Title => MonthTitle;

    public static IReadOnlyList<string> WeekdayLetters => CalendarViewModel.WeekdayNames;

    public override void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (first, last) = Agenda.MonthGrid(today.Year, today.Month);
        var counts = Agenda.Build(first, last, services.Store.ListEvents(first, last), services.Store.ListTasks(), services.Documents.List(),
                services.Projects.Agenda(first, last, services.Me().Id))
            .Where(i => !i.Done).GroupBy(i => i.Date).ToDictionary(g => g.Key, g => g.Count());
        Days.Clear();
        for (var day = first; day <= last; day = day.AddDays(1))
            Days.Add(new MonthDay(day, day == today, day.Month != today.Month, counts.GetValueOrDefault(day)));
        MonthTitle = today.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    private void OpenCalendar() => services.Go.To(Page.Calendar);
}

/// <summary>A note on the page: what's typed is kept a moment after typing stops.</summary>
public sealed partial class NoteBody : WidgetBody
{
    private readonly string widgetId;
    private readonly MyPagesViewModel page;
    private readonly DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool loading = true;

    public NoteBody(Widget widget, MyPagesViewModel page)
    {
        widgetId = widget.Id;
        this.page = page;
        Text = widget.Text ?? "";
        loading = false;
        typing.Tick += (_, _) => Keep();
    }

    [ObservableProperty] public partial string Text { get; set; }

    public bool HasTyped { get; private set; }

    partial void OnTextChanged(string value)
    {
        if (loading)
            return;
        HasTyped = true;
        typing.Stop();
        typing.Start();
    }

    public override void Keep()
    {
        typing.Stop();
        if (!HasTyped)
            return;
        HasTyped = false;
        page.SetText(widgetId, Text);
    }

    public override void Dispose()
    {
        Keep();
        typing.Stop();
        base.Dispose();
    }
}

/// <summary>The calculator, as in its own window.</summary>
public sealed class CalculatorBody(ToolsViewModel tools) : WidgetBody
{
    public CalculatorToolViewModel Tools { get; } = tools.Calculator;
}

/// <summary>The clipboard history, as in its own window.</summary>
public sealed class ClipboardBody(ToolsViewModel tools) : WidgetBody
{
    public ClipboardToolViewModel Tools { get; } = tools.Clipboard;
}

/// <summary>The colours tool, as in its own window.</summary>
public sealed class ColoursBody(ToolsViewModel tools) : WidgetBody
{
    public ColoursToolViewModel Tools { get; } = tools.Colours;
}

/// <summary>The time timer: start it with a note, stop it to keep the time, as on the Time page.</summary>
public sealed partial class TimerBody(AppServices services) : WidgetBody
{
    public TimerClock Timer { get; } = services.Timer;

    [ObservableProperty] public partial string Note { get; set; } = "";
    [ObservableProperty] public partial string Message { get; private set; } = "";

    [RelayCommand]
    private void Start()
    {
        if (Timer.IsRunning)
            return;
        Timer.Start(new RunningTimer { Started = DateTimeOffset.Now, Note = Note.Trim() });
        Message = "";
    }

    [RelayCommand]
    private void Stop()
    {
        var entry = Timer.StopAndKeep(Note.Trim());
        Message = entry is null ? "Stopped within half a minute, so nothing was kept." : $"Kept {TimeRules.Format(entry.Minutes)}.";
        if (entry is not null)
            Note = "";
    }
}

/// <summary>One client's card: who to talk to and how, with a way to their page or a new mail.</summary>
public sealed partial class ClientBody : WidgetBody
{
    private readonly AppServices services;
    private readonly string? clientId;

    public ClientBody(AppServices services, string? clientId)
    {
        this.services = services;
        this.clientId = clientId;
        Refresh();
    }

    [ObservableProperty] public partial Client? Client { get; private set; }

    public override string? Title => Client?.Name ?? "A client";

    public bool IsMissing => Client is null;
    public string Person => Client?.ContactPerson ?? "";
    public string Email => Client?.Email ?? "";
    public string Phone => Client?.Phone ?? "";
    public string Website => Client?.Website ?? "";
    public string Notes => Client?.Notes ?? "";

    public override void Refresh()
    {
        Client = clientId is null ? null : services.Clients.List().FirstOrDefault(c => c.Id == clientId);
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void Open()
    {
        if (Client is { } client)
            services.Go.Client(client);
    }

    [RelayCommand]
    private void Write()
    {
        if (Client is { Email: { Length: > 0 } email } client)
            services.Go.WriteMail([new Webignite.Core.Mail.MailPerson(client.ContactPerson ?? client.Name, email)]);
    }
}

/// <summary>Overview's Needs you: what wants doing, each with a way to it.</summary>
public sealed partial class NeedsYouBody : WidgetBody
{
    private readonly AppServices services;

    public NeedsYouBody(AppServices services)
    {
        this.services = services;
        services.RepliesChanged += Refresh;
        Refresh();
    }

    public ObservableCollection<SummaryLineViewModel> Lines { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; private set; }

    public override void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        Lines.Clear();
        foreach (var line in Webignite.Core.Overview.MorningSummary.Build(Summaries.Input(services, today, services.Documents.List())))
            Lines.Add(new SummaryLineViewModel(line, services));
        IsEmpty = Lines.Count == 0;
    }

    public override void Dispose()
    {
        services.RepliesChanged -= Refresh;
        base.Dispose();
    }
}

/// <summary>A mail in the inbox widget: who from, what about, when; a click opens it in Mail.</summary>
public sealed partial class MailLine(Webignite.Core.Mail.MailSummary mail, AppServices services)
{
    public string From => mail.From is { } from ? string.IsNullOrWhiteSpace(from.Name) ? from.Address : from.Name! : "";
    public string Subject => mail.Subject.Length > 0 ? mail.Subject : "(no subject)";
    public bool Unread => !mail.Seen;

    public string When => mail.Date.LocalDateTime.Date == DateTime.Today
        ? mail.Date.LocalDateTime.ToString("HH:mm", CultureInfo.CurrentCulture)
        : mail.Date.LocalDateTime.ToString("d MMM", CultureInfo.CurrentCulture);

    [RelayCommand]
    private void Open() => services.Go.Mail(mail);
}

/// <summary>The newest mail across every inbox, unread in bold; or, given a client, the newest mail to and from that client in any folder.</summary>
public sealed partial class MailBody : WidgetBody
{
    private const int Shown = 30;
    private readonly AppServices services;
    private readonly string? clientId;
    private readonly bool forClient;

    public MailBody(AppServices services)
    {
        this.services = services;
        Refresh();
    }

    public MailBody(AppServices services, string? clientId)
    {
        this.services = services;
        this.clientId = clientId;
        forClient = true;
        Refresh();
    }

    public ObservableCollection<MailLine> Lines { get; } = [];

    [ObservableProperty] public partial string Heading { get; private set; } = "Mail inbox";
    [ObservableProperty] public partial bool IsEmpty { get; private set; }

    /// <summary>What to say when there is nothing to list.</summary>
    [ObservableProperty] public partial string EmptyText { get; private set; } = "No mail here yet. Add a mailbox under Mail.";

    /// <summary>The client whose mail this is; null for the inbox widget, or when the client has gone.</summary>
    public Client? Client { get; private set; }
    public bool IsForClient => forClient;
    public bool CanWrite => Client is { Email.Length: > 0 };
    public string OpenText => forClient ? "Open the client" : "Open Mail";

    public override string? Title => Heading;

    public override void Refresh()
    {
        Lines.Clear();
        if (forClient)
            ShowClient();
        else
        {
            var inbox = Summaries.Inbox(services, Shown);
            foreach (var mail in inbox.OrderByDescending(m => m.Date).Take(Shown))
                Lines.Add(new MailLine(mail, services));
            var unread = inbox.Count(m => !m.Seen);
            Heading = unread == 0 ? "Mail inbox" : $"Mail inbox, {unread} unread";
        }
        IsEmpty = Lines.Count == 0;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanWrite));
    }

    // Mail to or from the client's own addresses and domain, wherever it was filed, newest first.
    private void ShowClient()
    {
        Client = clientId is null ? null : services.Clients.List().FirstOrDefault(c => c.Id == clientId);
        if (Client is not { } client)
        {
            Heading = "A client's mail";
            EmptyText = "This client is no longer in the app.";
            return;
        }
        var theirs = new Webignite.Core.Mail.ClientMail(client);
        var found = theirs.IsEmpty ? [] : services.Store.FindMail(services.Store.ListMailAccounts().Select(a => a.Id), theirs.Matches, Shown);
        foreach (var mail in found.OrderByDescending(m => m.Date).Take(Shown))
            Lines.Add(new MailLine(mail, services));
        var unread = found.Count(m => !m.Seen);
        Heading = unread == 0 ? $"Mail, {client.Name}" : $"Mail, {client.Name}, {unread} unread";
        EmptyText = theirs.IsEmpty
            ? "This client has no email address or website yet, so their mail can't be told apart. Add one on the client."
            : "No mail to or from this client yet.";
    }

    [RelayCommand]
    private void OpenMail()
    {
        if (!forClient)
            services.Go.To(Page.Mail);
        else if (Client is { } client)
            services.Go.Client(client);
    }

    /// <summary>A new mail to the client. Nothing is sent until Send is clicked.</summary>
    [RelayCommand]
    private void Write()
    {
        if (Client is { Email: { Length: > 0 } email } client)
            services.Go.WriteMail([new Webignite.Core.Mail.MailPerson(client.ContactPerson ?? client.Name, email)]);
    }
}

/// <summary>
/// A small file explorer: a folder's files and folders, folders first. A folder opens in place and the arrow goes
/// back up; a file opens in its program, and drags out to mail or another program. It remembers its folder and
/// follows changes to it while the page is open; closing the page lets the watcher go.
/// </summary>
public sealed partial class FilesBody : WidgetBody
{
    private readonly string widgetId;
    private readonly AppServices services;
    private readonly MyPagesViewModel page;
    private FileSystemWatcher? watcher;
    private readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private int reading;

    // Set once the page lets the widget go: changes still on their way from the watcher, and reads still going,
    // then do nothing, so the timer isn't started again after it's been stopped.
    private bool disposed;

    public FilesBody(Widget widget, AppServices services, MyPagesViewModel page)
    {
        widgetId = widget.Id;
        this.services = services;
        this.page = page;
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            if (!disposed)
                Refresh();
        };
        Folder = MyPages.StartFolder(widget.Target, Directory.Exists, Webignite.Desktop.Files.FileOps.ClientRoot(services.Settings.Files),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Watch();
        Refresh();
    }

    public ObservableCollection<FileLine> Lines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderName), nameof(CanGoUp))]
    public partial string Folder { get; private set; }

    public string FolderName => Webignite.Core.Files.FilePaths.Crumbs(Folder) is { Count: > 0 } crumbs ? crumbs[^1].Name : Folder;
    public bool CanGoUp => Webignite.Core.Files.FilePaths.Parent(Folder) is not null;

    public override string? Title => FolderName;

    [ObservableProperty] public partial string Empty { get; private set; } = "";

    public bool IsEmpty => Empty.Length > 0;

    partial void OnEmptyChanged(string value) => OnPropertyChanged(nameof(IsEmpty));

    /// <summary>Reads the folder off the screen's thread; a newer read wins over one still going.</summary>
    public override void Refresh() => ReadAsync().Forget("Reading a folder for the Files widget");

    private async Task ReadAsync()
    {
        if (disposed)
            return;
        var mine = ++reading;
        var folder = Folder;
        IReadOnlyList<Webignite.Core.Files.FileEntry>? entries = null;
        var problem = "";
        await Task.Run(() =>
        {
            try
            {
                entries = Webignite.Core.Files.FileLists.Sort(Webignite.Desktop.Files.FileOps.List(folder), Webignite.Core.Files.FileSort.Name, false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                problem = Directory.Exists(folder) ? "This folder can't be read." : "This folder isn't there any more. Pick another.";
            }
        });
        if (mine != reading || disposed)
            return;
        Lines.Clear();
        foreach (var entry in entries ?? [])
            Lines.Add(new FileLine(entry, this));
        Empty = problem.Length > 0 ? problem : Lines.Count == 0 ? "Nothing in this folder yet." : "";
    }

    /// <summary>Shows another folder, and keeps it with the page.</summary>
    public void Go(string folder)
    {
        if (string.Equals(folder, Folder, StringComparison.OrdinalIgnoreCase))
            return;
        Folder = folder;
        OnPropertyChanged(nameof(Title));
        page.SetTarget(widgetId, folder);
        Watch();
        Refresh();
    }

    [RelayCommand]
    private void Up()
    {
        if (Webignite.Core.Files.FilePaths.Parent(Folder) is { } parent)
            Go(parent);
    }

    [RelayCommand]
    private void OpenInFiles() => services.Go.Folder(Folder);

    internal void OpenInFiles(string folder) => services.Go.Folder(folder);

    private void Watch()
    {
        watcher?.Dispose();
        watcher = null;
        if (!Directory.Exists(Folder))
            return;
        try
        {
            watcher = new FileSystemWatcher(Folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            var watching = watcher;
            watcher.Created += (_, _) => Dispatcher.UIThread.Post(Settle);
            watcher.Deleted += (_, _) => Dispatcher.UIThread.Post(Settle);
            watcher.Changed += (_, _) => Dispatcher.UIThread.Post(Settle);
            watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(Settle);
            // Too many changes at once, or the folder gone or unreachable for a moment: read it again and watch
            // it afresh, unless the widget has moved on to another folder or been let go meanwhile.
            watcher.Error += (_, e) => Dispatcher.UIThread.Post(() =>
            {
                if (disposed || !ReferenceEquals(watcher, watching))
                    return;
                Webignite.Data.AppLog.Write("Watching a folder for the Files widget", e.GetException());
                Watch();
                Refresh();
            });
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Webignite.Data.AppLog.Write("Watching a folder for the Files widget", e);
            watcher?.Dispose();
            watcher = null;
        }
    }

    // A change seen: read the folder once things have been quiet a moment.
    private void Settle()
    {
        if (disposed)
            return;
        settle.Stop();
        settle.Start();
    }

    public override void Dispose()
    {
        disposed = true;
        settle.Stop();
        watcher?.Dispose();
        watcher = null;
        base.Dispose();
    }
}

/// <summary>A file or folder in the Files widget.</summary>
public sealed partial class FileLine(Webignite.Core.Files.FileEntry entry, FilesBody body) : ObservableObject
{
    public Webignite.Core.Files.FileEntry Entry { get; } = entry;
    public FilesBody Body { get; } = body;
    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public bool IsFolder => Entry.IsFolder;
    public bool IsFile => !Entry.IsFolder;
    public string About => Entry.IsFolder ? "Folder" : Webignite.Core.Files.FileSizes.Format(Entry.Size);

    /// <summary>A folder opens in the widget; a file opens in its program.</summary>
    [RelayCommand]
    private void Open()
    {
        if (IsFolder)
            Body.Go(Path);
        else if (File.Exists(Path))
            Webignite.Desktop.Files.FileOps.Open(Path);
        else
            Body.Refresh();
    }

    /// <summary>The Files page, at the folder or the folder the file is in.</summary>
    [RelayCommand]
    private void ShowInFiles() => Body.OpenInFiles(IsFolder ? Path : Webignite.Core.Files.FilePaths.Parent(Path) ?? Path);
}
