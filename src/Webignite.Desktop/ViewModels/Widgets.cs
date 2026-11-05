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
        _ => new SoonBody(),
    };
}

/// <summary>A kind not built yet; kept on the page so nothing is lost.</summary>
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
        foreach (var (group, tasks) in ProjectRules.ByWhen(services.Projects.Mine(services.Me().Id), today))
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
    public ToolsViewModel Tools { get; } = tools;
}

/// <summary>The clipboard history, as in its own window.</summary>
public sealed class ClipboardBody(ToolsViewModel tools) : WidgetBody
{
    public ToolsViewModel Tools { get; } = tools;
}

/// <summary>The colours tool, as in its own window.</summary>
public sealed class ColoursBody(ToolsViewModel tools) : WidgetBody
{
    public ToolsViewModel Tools { get; } = tools;
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
