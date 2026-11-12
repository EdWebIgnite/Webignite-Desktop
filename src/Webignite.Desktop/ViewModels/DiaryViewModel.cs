using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Calendar;
using Webignite.Core.Tasks;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The diary: a day to a page, two pages open like a book. Everything on a page is the calendar's and the
/// projects' own (events, to-dos, project tasks, money dates), so what is written or ticked here shows there too;
/// only the notes at the foot of a page are the diary's.
/// </summary>
public partial class DiaryViewModel : ViewModelBase, IRefreshable, IHoldsWork
{
    private readonly AppServices services;
    private DateOnly left;

    public DiaryViewModel(AppServices services)
    {
        this.services = services;
        left = Diary.Spread(Today).Left;
        Left = Right = null!;
        Fill();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    [ObservableProperty] public partial DiaryPageViewModel Left { get; private set; }
    [ObservableProperty] public partial DiaryPageViewModel Right { get; private set; }

    /// <summary>What the two open pages cover: "12 and 13 November 2026".</summary>
    [ObservableProperty] public partial string Title { get; private set; } = "";

    /// <summary>The day picked in the date box, to open the diary at.</summary>
    public DateTime? OpenAt
    {
        get => left.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (value is { } day)
                Show(DateOnly.FromDateTime(day));
        }
    }

    /// <summary>Something is half typed on a page, so the page is left as it is until it is written in or dropped.</summary>
    public bool HoldsWork => Left.HasTyping || Right.HasTyping;

    [RelayCommand]
    private void PreviousPages() => Show(left.AddDays(-2));

    [RelayCommand]
    private void NextPages() => Show(left.AddDays(2));

    [RelayCommand]
    private void GoToToday() => Show(Today);

    /// <summary>Opens the diary at a day, on whichever of its two pages the day falls.</summary>
    public void Show(DateOnly day)
    {
        Keep();
        left = Diary.Spread(day).Left;
        Fill();
        OnPropertyChanged(nameof(OpenAt));
    }

    /// <summary>Keeps the notes typed on the open pages.</summary>
    public void Keep()
    {
        Left.KeepNotes();
        Right.KeepNotes();
    }

    public void Refresh()
    {
        Keep();
        Fill();
    }

    private void Fill()
    {
        var right = left.AddDays(1);
        var items = Agenda.Build(left, right, services.Store.ListEvents(left, right), services.Store.ListTasks(), services.Documents.List(),
            [.. services.Projects.Agenda(left, right, services.Me().Id), .. Diary.MoneyDates(left, right, services.Store.ListRenewals(), services.Store.ListRetainers())]);
        Left = new DiaryPageViewModel(Diary.Day(left, items), Today, services, this);
        Right = new DiaryPageViewModel(Diary.Day(right, items), Today, services, this);
        Title = left.Month == right.Month
            ? $"{left.Day} and {right.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)}"
            : $"{left.ToString("d MMMM", CultureInfo.CurrentCulture)} and {right.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)}";
    }
}

/// <summary>One page of the diary: a day, its hours, what has no time, and a few lines of notes.</summary>
public partial class DiaryPageViewModel : ViewModelBase
{
    private readonly AppServices services;
    private readonly DiaryViewModel diary;
    private string keptNotes;

    public DiaryPageViewModel(DiaryDay day, DateOnly today, AppServices services, DiaryViewModel diary)
    {
        this.services = services;
        this.diary = diary;
        Date = day.Date;
        IsToday = day.Date == today;
        foreach (var line in day.Lines)
            Lines.Add(new DiaryLineViewModel(line, this));
        foreach (var item in day.Untimed)
            Untimed.Add(new DiaryEntryViewModel(item, this));
        keptNotes = services.Store.DiaryNote(day.Date);
        Notes = keptNotes;
    }

    public DateOnly Date { get; }
    public bool IsToday { get; }
    public string DayNumber => Date.Day.ToString(CultureInfo.CurrentCulture);
    public string DayName => Date.ToString("dddd", CultureInfo.CurrentCulture);
    public string MonthYear => Date.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    public ObservableCollection<DiaryLineViewModel> Lines { get; } = [];
    public ObservableCollection<DiaryEntryViewModel> Untimed { get; } = [];
    public bool HasUntimed => Untimed.Count > 0;

    /// <summary>A to-do being typed for this day.</summary>
    [ObservableProperty] public partial string NewTodo { get; set; } = "";

    /// <summary>The free lines at the foot of the page.</summary>
    [ObservableProperty] public partial string Notes { get; set; }

    /// <summary>Why the last thing typed didn't take; empty when it did.</summary>
    [ObservableProperty] public partial string Message { get; set; } = "";

    public bool HasTyping => NewTodo.Trim().Length > 0 || Notes != keptNotes || Lines.Any(l => l.NewText.Trim().Length > 0);

    [RelayCommand]
    private void AddTodo()
    {
        var title = NewTodo.Trim();
        if (title.Length == 0)
            return;
        services.Store.SaveTask(new TaskItem { Id = TaskItem.NewId(), Title = title, Due = Date });
        NewTodo = "";
        diary.Refresh();
    }

    public void KeepNotes()
    {
        if (Notes == keptNotes)
            return;
        services.Store.SaveDiaryNote(Date, Notes);
        keptNotes = Notes;
    }

    /// <summary>Writes what was typed on an hour's line into the calendar. "9:30 Call Pieter" starts at half past; without a time it starts on the hour.</summary>
    internal void AddAt(DiaryLineViewModel line)
    {
        var typed = line.NewText.Trim();
        if (typed.Length == 0)
            return;
        var start = line.Hour;
        var space = typed.IndexOf(' ');
        if (space > 0 && typed[..space].Any(c => c is ':' or 'h' or '.') && CalendarEvents.TryParseTime(typed[..space], out var said) && typed[(space + 1)..].Trim().Length > 0)
        {
            start = said;
            typed = typed[(space + 1)..].Trim();
        }
        services.Store.SaveEvent(new CalendarEvent { Id = CalendarEvent.NewId(), Title = typed, Date = Date, Start = start, End = start.AddHours(1) });
        line.NewText = "";
        diary.Refresh();
    }

    internal void Tick(DiaryEntryViewModel entry, bool done)
    {
        if (entry.Item.Kind == AgendaKind.Task)
            services.Store.ChangeTask(entry.Item.SourceId, t => t with { Done = done, DoneAt = done ? DateTimeOffset.UtcNow : null });
        else if (entry.Item.Kind == AgendaKind.ProjectTask)
        {
            var (saved, problems) = services.Projects.ChangeTask(entry.Item.SourceId, t => t with { Done = done });
            if (saved is null)
            {
                diary.Refresh();
                (diary.Left.Date == Date ? diary.Left : diary.Right).Message = string.Join(" ", problems);
                return;
            }
        }
        diary.Refresh();
    }

    internal void Delete(DiaryEntryViewModel entry)
    {
        if (services.Store.GetEvent(entry.Item.SourceId) is not { } gone)
            return;
        services.Store.DeleteEvent(gone.Id);
        diary.Refresh();
        services.OfferUndo($"Deleted {gone.Title}", () =>
        {
            services.Store.SaveEvent(gone);
            diary.Refresh();
        });
    }

    /// <summary>Opens what an entry is about: the task in its project, the invoice or quote, the renewals or retainers page.</summary>
    internal void Open(DiaryEntryViewModel entry)
    {
        switch (entry.Item.Kind)
        {
            case AgendaKind.ProjectTask:
                services.Go.ProjectTask(entry.Item.SourceId);
                break;
            case AgendaKind.InvoiceDue or AgendaKind.QuoteExpires when services.Documents.Get(entry.Item.SourceId) is { } stored:
                services.Go.Document(stored);
                break;
            case AgendaKind.Renewal:
                services.Go.To(Page.Renewals);
                break;
            case AgendaKind.Retainer:
                services.Go.To(Page.Retainers);
                break;
        }
    }

    internal string? JoinLinkOf(DiaryEntryViewModel entry) =>
        entry.Item.Kind == AgendaKind.Event ? services.Store.GetEvent(entry.Item.SourceId)?.JoinLink : null;

    internal string? PlaceOf(DiaryEntryViewModel entry) =>
        entry.Item.Kind == AgendaKind.Event ? services.Store.GetEvent(entry.Item.SourceId)?.Location : null;
}

/// <summary>An hour's ruled line: what is on it, and room to write something in.</summary>
public partial class DiaryLineViewModel(DiaryLine line, DiaryPageViewModel page) : ViewModelBase
{
    public TimeOnly Hour => line.Hour;
    public string HourText => line.Hour.ToString("HH", CultureInfo.InvariantCulture);
    public IReadOnlyList<DiaryEntryViewModel> Entries { get; } = [.. line.Items.Select(i => new DiaryEntryViewModel(i, page))];

    [ObservableProperty] public partial string NewText { get; set; } = "";

    public string WriteHint => $"Write in at {line.Hour:HH:mm}";

    [RelayCommand]
    private void Add() => page.AddAt(this);
}

/// <summary>One thing written on a diary page.</summary>
public partial class DiaryEntryViewModel : ViewModelBase
{
    private readonly DiaryPageViewModel page;

    public DiaryEntryViewModel(AgendaItem item, DiaryPageViewModel page)
    {
        this.page = page;
        Item = item;
        JoinLink = page.JoinLinkOf(this);
        Place = page.PlaceOf(this) ?? "";
    }

    public AgendaItem Item { get; }
    public string Title => Item.Title;
    public string Time => Item.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
    public bool HasTime => Item.Time is not null;
    public bool CanTick => Item.Kind is AgendaKind.Task or AgendaKind.ProjectTask;
    public bool IsPlain => !CanTick;
    public bool IsEvent => Item.Kind == AgendaKind.Event;
    public bool CanOpen => Item.Kind is AgendaKind.ProjectTask or AgendaKind.InvoiceDue or AgendaKind.QuoteExpires or AgendaKind.Renewal or AgendaKind.Retainer;
    public string? JoinLink { get; }
    public bool CanJoin => JoinLink is not null;
    public string Place { get; }
    public bool HasPlace => Place.Length > 0;

    public string KindLabel => Item.Kind switch
    {
        AgendaKind.Event => "",
        AgendaKind.Task => "To-do",
        AgendaKind.ProjectTask => "Project task",
        AgendaKind.QuoteExpires => "Quote",
        AgendaKind.InvoiceDue => "Invoice",
        AgendaKind.Renewal => "Renewal",
        _ => "Retainer",
    };

    public bool HasKind => KindLabel.Length > 0;

    public bool IsDone
    {
        get => Item.Done;
        set
        {
            if (value != Item.Done)
                page.Tick(this, value);
        }
    }

    [RelayCommand]
    private void Delete() => page.Delete(this);

    [RelayCommand]
    private void Open() => page.Open(this);

    [RelayCommand]
    private void Join()
    {
        if (JoinLink is { } link && Uri.TryCreate(link, UriKind.Absolute, out var url))
            AppServices.OpenInBrowser(url).Forget("Opening a meeting's link", tell: true);
    }
}
