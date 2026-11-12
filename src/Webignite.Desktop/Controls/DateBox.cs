using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Metadata;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A date, picked from a small calendar. The box shows the day as the PC's region writes it; a click opens a
/// month to pick from, with a place to type a date, Today and Clear. The week starts on the day the PC's region
/// says it does. What a page puts in <see cref="Extra"/> is shown under the month (how a task repeats, its time),
/// and the pop-up then stays open after a day is picked so that can be set too.
/// The pop-up is made when it is opened and let go when it closes.
/// </summary>
public sealed class DateBox : Button
{
    public static readonly StyledProperty<DateTime?> SelectedDateProperty =
        AvaloniaProperty.Register<DateBox, DateTime?>(nameof(SelectedDate), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<DateBox, string>(nameof(PlaceholderText), "Pick a date");

    public static readonly StyledProperty<Control?> ExtraProperty = AvaloniaProperty.Register<DateBox, Control?>(nameof(Extra));

    /// <summary>The first and the last day that can be picked, when a date mustn't be in the past (send later) or the future (a payment).</summary>
    public static readonly StyledProperty<DateTime?> EarliestProperty = AvaloniaProperty.Register<DateBox, DateTime?>(nameof(Earliest));
    public static readonly StyledProperty<DateTime?> LatestProperty = AvaloniaProperty.Register<DateBox, DateTime?>(nameof(Latest));

    /// <summary>Words after the date in the box, such as a task's time or how it repeats.</summary>
    public static readonly StyledProperty<string?> NoteProperty = AvaloniaProperty.Register<DateBox, string?>(nameof(Note));

    private readonly TextBlock words = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    private DateTime month = DateTime.Today;
    private Flyout? open;
    private Grid? days;
    private TextBlock? monthTitle;
    private TextBox? typed;

    public DateBox()
    {
        Classes.Add("datebox");
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var icon = new Icon { Size = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        icon.Bind(Icon.DataProperty, this.GetResourceObservable("Icon.Calendar"));
        icon.Bind(Icon.ForegroundProperty, this.GetResourceObservable("Muted"));
        DockPanel.SetDock(icon, Dock.Right);
        Content = new DockPanel { Children = { icon, words } };
        ShowWords();
    }

    // Styled as the app's own date box (Styles/Controls.axaml), not as whatever a plain button looks like.
    protected override Type StyleKeyOverride => typeof(Button);

    public DateTime? SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public string PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    [Content]
    public Control? Extra
    {
        get => GetValue(ExtraProperty);
        set => SetValue(ExtraProperty, value);
    }

    public DateTime? Earliest
    {
        get => GetValue(EarliestProperty);
        set => SetValue(EarliestProperty, value);
    }

    public DateTime? Latest
    {
        get => GetValue(LatestProperty);
        set => SetValue(LatestProperty, value);
    }

    /// <summary>Whether a day is one that may be picked, between the earliest and the latest when those are set.</summary>
    public static bool Allowed(DateTime day, DateTime? earliest, DateTime? latest) =>
        (earliest is not { } first || day.Date >= first.Date) && (latest is not { } last || day.Date <= last.Date);

    public string? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    /// <summary>How a day is written in the box and typed into the pop-up: "12 Nov 2026" in the PC's own language.</summary>
    public static string Written(DateTime day) => day.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>A typed day, read as the PC's region writes dates; null when it isn't one.</summary>
    public static DateTime? Read(string? text) =>
        DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var day) ? day.Date : null;

    /// <summary>The weeks a month's page shows: six rows of seven days from the week its 1st is in, starting on the given weekday.</summary>
    public static IReadOnlyList<DateTime> PageOf(DateTime month, DayOfWeek weekStarts)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var start = first.AddDays(-(((int)first.DayOfWeek - (int)weekStarts + 7) % 7));
        return [.. Enumerable.Range(0, 42).Select(i => start.AddDays(i))];
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedDateProperty || change.Property == PlaceholderTextProperty || change.Property == NoteProperty)
        {
            ShowWords();
            if (change.Property == SelectedDateProperty && open is not null)
                FillMonth();
        }
    }

    private void ShowWords()
    {
        var picked = SelectedDate is { } day;
        words.Text = SelectedDate is { } shown ? (string.IsNullOrEmpty(Note) ? Written(shown) : $"{Written(shown)}, {Note}") : PlaceholderText;
        words.Classes.Set("muted", !picked);
        // A narrow box can't show a time and a repeat after the date, so the whole of it is also the tip.
        if (!string.IsNullOrEmpty(Note))
            ToolTip.SetTip(this, words.Text);
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (open is not null)
            return;
        month = SelectedDate ?? DateTime.Today;
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, Content = Build() };
        flyout.Closed += (_, _) =>
        {
            open = null;
            days = null;
            monthTitle = null;
            typed = null;
            // Handed back to the page, so it can be shown again next time.
            if (Extra is { Parent: ContentControl host })
                host.Content = null;
        };
        open = flyout;
        FillMonth();
        flyout.ShowAt(this);
    }

    /// <summary>What the pop-up shows, built without opening it. For the screenshot tool, which can't draw pop-ups.</summary>
    public Control Picture()
    {
        month = SelectedDate ?? DateTime.Today;
        var all = Build();
        FillMonth();
        return all;
    }

    private StackPanel Build()
    {
        typed = new TextBox { PlaceholderText = "Type a date", MinWidth = 150, Text = SelectedDate is { } day ? Written(day) : "" };
        typed.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            TakeTyped();
            e.Handled = true;
        };
        typed.LostFocus += (_, _) => TakeTyped();
        var clear = Small("Clear", () =>
        {
            SelectedDate = null;
            open?.Hide();
        });
        DockPanel.SetDock(clear, Dock.Right);
        var top = new DockPanel { Children = { clear, typed } };

        monthTitle = new TextBlock { FontWeight = Avalonia.Media.FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var back = Arrow("Icon.ChevronLeft", "The month before", -1);
        var on = Arrow("Icon.ChevronRight", "The month after", 1);
        DockPanel.SetDock(back, Dock.Left);
        DockPanel.SetDock(on, Dock.Right);
        var head = new DockPanel { Margin = new Thickness(0, 12, 0, 6), Children = { back, on, monthTitle } };

        days = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto") };

        var today = Small("Today", () => Pick(DateTime.Today));
        today.IsEnabled = Allowed(DateTime.Today, Earliest, Latest);
        var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0), Children = { today } };
        today.HorizontalAlignment = HorizontalAlignment.Left;

        var all = new StackPanel { Width = 280, Children = { top, head, days } };
        if (Extra is { } extra)
        {
            var line = new Border { Height = 1, Margin = new Thickness(0, 12) };
            line.Bind(Border.BackgroundProperty, this.GetResourceObservable("Line"));
            all.Children.Add(line);
            // The page's own controls, still reading the page's data.
            all.Children.Add(new ContentControl { Content = extra, DataContext = DataContext });
        }
        all.Children.Add(foot);
        return all;
    }

    private Button Small(string text, Action clicked)
    {
        var button = new Button { Content = text };
        button.Classes.Add("link");
        button.Click += (_, _) => clicked();
        return button;
    }

    private Button Arrow(string icon, string tip, int months)
    {
        var picture = new Icon { Size = 14 };
        picture.Bind(Icon.DataProperty, this.GetResourceObservable(icon));
        var button = new Button { Content = picture };
        button.Classes.Add("calnav");
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) =>
        {
            month = month.AddMonths(months);
            FillMonth();
        };
        return button;
    }

    private void TakeTyped()
    {
        if (typed is null || string.IsNullOrWhiteSpace(typed.Text))
            return;
        if (Read(typed.Text) is { } day && Allowed(day, Earliest, Latest))
        {
            month = day;
            SelectedDate = day;
        }
        else if (SelectedDate is { } kept)
            typed.Text = Written(kept); // not a date: back to what it was
    }

    private void Pick(DateTime day)
    {
        month = day;
        SelectedDate = day.Date;
        if (typed is not null)
            typed.Text = Written(day);
        // With more to set under the month (a repeat, a time) the pop-up stays; otherwise picking a day is the whole job.
        if (Extra is null)
            open?.Hide();
        else
            FillMonth();
    }

    private void FillMonth()
    {
        if (days is null || monthTitle is null)
            return;
        var culture = CultureInfo.CurrentCulture;
        monthTitle.Text = month.ToString("MMMM yyyy", culture);
        days.Children.Clear();
        var weekStarts = culture.DateTimeFormat.FirstDayOfWeek;
        var page = PageOf(month, weekStarts);
        for (var column = 0; column < 7; column++)
        {
            var name = new TextBlock
            {
                Text = culture.DateTimeFormat.GetShortestDayName(page[column].DayOfWeek),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4),
            };
            name.Classes.Add("label");
            Grid.SetColumn(name, column);
            days.Children.Add(name);
        }
        for (var i = 0; i < page.Count; i++)
        {
            var day = page[i];
            var button = new Button { Content = day.Day.ToString(culture) };
            button.Classes.Add("calday");
            button.Classes.Set("other", day.Month != month.Month);
            button.Classes.Set("today", day.Date == DateTime.Today);
            button.Classes.Set("picked", SelectedDate is { } picked && picked.Date == day.Date);
            button.IsEnabled = Allowed(day, Earliest, Latest);
            button.Click += (_, _) => Pick(day);
            Grid.SetColumn(button, i % 7);
            Grid.SetRow(button, i / 7 + 1);
            days.Children.Add(button);
        }
    }
}
