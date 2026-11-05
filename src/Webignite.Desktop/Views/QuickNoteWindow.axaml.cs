using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Webignite.Core.Calendar;
using Webignite.Core.Notes;
using Webignite.Core.Tasks;
using Webignite.Core.Tools;

namespace Webignite.Desktop.Views;

/// <summary>
/// The quick box from the shortcut (Ctrl+Alt+N), over whatever program is in front: what is typed becomes a note,
/// a to-do or a calendar event. It's kept when the box closes, however it closes, unless Discard is pressed: a
/// thought caught in a hurry isn't lost. An event whose time can't be read is kept as a note instead.
/// </summary>
public partial class QuickNoteWindow : Window
{
    private readonly AppServices? services;
    private bool done;

    /// <summary>For the designer only.</summary>
    public QuickNoteWindow() => InitializeComponent();

    public QuickNoteWindow(AppServices services)
    {
        this.services = services;
        InitializeComponent();
        var notebooks = services.Notes.Notebooks();
        NotebookBox.ItemsSource = notebooks;
        NotebookBox.SelectedItem = notebooks.FirstOrDefault(n => n.Id == services.Settings.Notes.QuickNotebookId) ?? notebooks[0];
        DueBox.ItemsSource = QuickCapture.DueChoices.Select(QuickCapture.DueLabel).ToList();
        DueBox.SelectedIndex = 0;
        DayBox.SelectedDate = DateTime.Today;
        // A problem with the time or day goes as soon as either is changed.
        TimeBox.TextChanged += (_, _) => Say(null);
        DayBox.SelectedDateChanged += (_, _) => Say(null);
        Show(CaptureKind.Note);
        AddHandler(KeyDownEvent, Keys, RoutingStrategies.Tunnel);
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Activate();
            Words.Focus();
        });
        Closing += (_, _) => Keep(force: true);
    }

    public CaptureKind Kind { get; private set; }

    private CaptureDue due = CaptureDue.NoDay;

    /// <summary>When a to-do is due. Setting it moves the list to match.</summary>
    public CaptureDue Due
    {
        get => due;
        set
        {
            due = value;
            var index = QuickCapture.DueChoices.ToList().IndexOf(value);
            if (DueBox.SelectedIndex != index)
                DueBox.SelectedIndex = index;
        }
    }

    /// <summary>What was saved, once the box is closed with something typed. One of the three is set.</summary>
    public Note? Saved { get; private set; }
    public TaskItem? SavedTodo { get; private set; }
    public CalendarEvent? SavedEvent { get; private set; }

    /// <summary>The words, for a screenshot of the box.</summary>
    public string Typed
    {
        get => Words.Text ?? "";
        set => Words.Text = value;
    }

    /// <summary>The time typed for an event, for the screenshot tool.</summary>
    public string Time
    {
        get => TimeBox.Text ?? "";
        set => TimeBox.Text = value;
    }

    public DateOnly? Day
    {
        get => DayBox.SelectedDate is { } d ? DateOnly.FromDateTime(d) : null;
        set => DayBox.SelectedDate = value?.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>Switches between note, to-do and event; what's typed stays.</summary>
    public void Show(CaptureKind kind)
    {
        Kind = kind;
        Title = QuickCapture.Title(kind);
        Heading.Text = QuickCapture.Title(kind);
        Words.PlaceholderText = QuickCapture.Hint(kind);
        AsNote.Classes.Set("on", kind == CaptureKind.Note);
        AsTodo.Classes.Set("on", kind == CaptureKind.Todo);
        AsEvent.Classes.Set("on", kind == CaptureKind.Event);
        NotebookBox.IsVisible = kind == CaptureKind.Note;
        TodoPart.IsVisible = kind == CaptureKind.Todo;
        EventPart.IsVisible = kind == CaptureKind.Event;
        Say(null);
    }

    private void Keys(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (e.Key == Key.Escape || (e.Key == Key.Enter && ctrl))
        {
            SaveAndClose();
            e.Handled = true;
        }
        else if (ctrl && e.Key is Key.D1 or Key.D2 or Key.D3 or Key.NumPad1 or Key.NumPad2 or Key.NumPad3)
        {
            Show(e.Key is Key.D1 or Key.NumPad1 ? CaptureKind.Note : e.Key is Key.D2 or Key.NumPad2 ? CaptureKind.Todo : CaptureKind.Event);
            e.Handled = true;
        }
    }

    private void NoteClicked(object? sender, RoutedEventArgs e) => Show(CaptureKind.Note);
    private void TodoClicked(object? sender, RoutedEventArgs e) => Show(CaptureKind.Todo);
    private void EventClicked(object? sender, RoutedEventArgs e) => Show(CaptureKind.Event);

    private void DuePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (DueBox.SelectedIndex >= 0)
            due = QuickCapture.DueChoices[DueBox.SelectedIndex];
    }

    private void SaveClicked(object? sender, RoutedEventArgs e) => SaveAndClose();

    private void DiscardClicked(object? sender, RoutedEventArgs e)
    {
        done = true;
        Close();
    }

    /// <summary>Saves and closes, or says what's wrong and stays open so it can be fixed.</summary>
    public void SaveAndClose()
    {
        if (Keep(force: false))
            Close();
    }

    /// <summary>
    /// Saves what was typed, once. Returns false, with the problem showing, when an event can't be saved as it
    /// stands; with <paramref name="force"/> (the box is closing anyway) it's kept as a note instead.
    /// </summary>
    public bool Keep(bool force)
    {
        if (done || services is null)
            return true;
        var typed = Words.Text ?? "";
        if (typed.Trim().Length == 0)
        {
            done = true;
            return true;
        }
        var now = DateTimeOffset.UtcNow;
        switch (Kind)
        {
            case CaptureKind.Todo when QuickCapture.Todo(typed, Due, DateOnly.FromDateTime(DateTime.Today), TaskItem.NewId(), now) is { } todo:
                done = true;
                SavedTodo = services.Store.SaveTask(todo);
                return true;
            case CaptureKind.Event:
                var (calendarEvent, problem) = QuickCapture.Event(typed, Day, Time, Guid.CreateVersion7().ToString("N"));
                if (calendarEvent is not null)
                {
                    done = true;
                    SavedEvent = services.Store.SaveEvent(calendarEvent);
                    return true;
                }
                if (!force)
                {
                    Say(problem);
                    return false;
                }
                // Closing anyway: kept as a note, with the time as typed, so nothing is lost.
                typed += Time.Trim().Length > 0 ? "\n" + Time.Trim() : "";
                break;
        }
        done = true;
        var notebook = (NotebookBox.SelectedItem as Notebook)?.Id;
        if (notebook is not null && notebook != services.Settings.Notes.QuickNotebookId)
            services.Settings.Notes = services.Settings.Notes with { QuickNotebookId = notebook };
        Saved = services.Notes.Quick(typed, notebook);
        return true;
    }

    private void Say(string? problem)
    {
        Problem.Text = problem ?? "";
        Problem.IsVisible = problem is not null;
    }
}
