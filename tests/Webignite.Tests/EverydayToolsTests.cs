using Webignite.Core.Tools;

namespace Webignite.Tests;

/// <summary>Clipboard history, the colour loupe, quick capture and the shortcuts that work from any program.</summary>
public sealed class EverydayToolsTests
{
    private static readonly DateOnly Thursday = new(2026, 11, 5);

    [Fact]
    public void Clipboard_history_keeps_the_newest_first_once_each_and_at_most_25()
    {
        IReadOnlyList<Clip> items = [];
        for (var i = 1; i <= 30; i++)
            items = ClipboardHistory.Add(items, new Clip("copy " + i));
        Assert.Equal(ClipboardHistory.Most, items.Count);
        Assert.Equal("copy 30", items[0].Text);
        items = ClipboardHistory.Add(items, new Clip("copy 20"));
        Assert.Equal("copy 20", items[0].Text);
        Assert.Single(items, i => i.Text == "copy 20");
    }

    [Fact]
    public void Blank_and_huge_copies_are_not_kept()
    {
        IReadOnlyList<Clip> items = [new Clip("kept")];
        Assert.Same(items, ClipboardHistory.Add(items, new Clip("   \n")));
        Assert.Same(items, ClipboardHistory.Add(items, new Clip(null)));
        Assert.Same(items, ClipboardHistory.Add(items, new Clip(new string('x', ClipboardHistory.LongestKept + 1))));
        Assert.Same(items, ClipboardHistory.Add(items, new Clip(null, new byte[ClipboardHistory.LargestPicture + 1], 10, 10)));
    }

    [Fact]
    public void Pictures_are_kept_once_each_and_only_the_newest_few()
    {
        IReadOnlyList<Clip> items = [];
        for (var i = 0; i < 12; i++)
            items = ClipboardHistory.Add(items, new Clip(null, [(byte)i, 1, 2], 1920, 1080));
        items = ClipboardHistory.Add(items, new Clip("text"));
        Assert.Equal(ClipboardHistory.MostPictures, items.Count(c => c.IsPicture));
        items = ClipboardHistory.Add(items, new Clip(null, [11, 1, 2], 1920, 1080));
        Assert.Single(items, c => c.Key == new Clip(null, [11, 1, 2]).Key);
        Assert.True(items[0].IsPicture);
        Assert.Equal("Picture, 1920 × 1080", ClipboardHistory.Size(items[0]));
    }

    [Fact]
    public void The_history_is_searched_by_its_words_and_picture_finds_the_pictures()
    {
        var text = new Clip("thandi@karoocoffee.co.za");
        var picture = new Clip(null, [1, 2, 3], 800, 600);
        Assert.True(ClipboardHistory.Matches(text, "karoo"));
        Assert.False(ClipboardHistory.Matches(text, "bosveld"));
        Assert.True(ClipboardHistory.Matches(picture, "picture"));
        Assert.False(ClipboardHistory.Matches(picture, "karoo"));
        Assert.True(ClipboardHistory.Matches(picture, ""));
    }

    [Fact]
    public void A_copy_shows_on_one_line_with_its_size()
    {
        Assert.Equal("Hi Pieter, the site is live", ClipboardHistory.Preview("Hi Pieter,\n\n  the site   is live"));
        Assert.EndsWith("…", ClipboardHistory.Preview(new string('a', 200)), StringComparison.Ordinal);
        Assert.StartsWith("2 lines", ClipboardHistory.Size(new Clip("one\ntwo")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(100, 100, 95, 95)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1919, 1079, 1909, 1069)]
    public void The_loupe_stays_inside_the_screen(int x, int y, int left, int top)
    {
        var area = ColourLoupe.Around(x, y, 1920, 1080);
        Assert.Equal((left, top, ColourLoupe.Reach * 2 + 1), (area.X, area.Y, area.Width));
        Assert.True(area.Contains(x, y));
    }

    [Fact]
    public void A_quick_todo_takes_the_first_line_as_its_title_and_a_due_day()
    {
        var todo = QuickCapture.Todo("Send Bosveld the sitemap\nThe PDF from Friday", CaptureDue.Tomorrow, Thursday, "t1", DateTimeOffset.UnixEpoch)!;
        Assert.Equal(("Send Bosveld the sitemap", "The PDF from Friday", new DateOnly(2026, 11, 6)), (todo.Title, todo.Notes, todo.Due));
        Assert.Null(QuickCapture.Todo("  \n ", CaptureDue.Today, Thursday, "t2", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Next_week_is_the_coming_Monday()
    {
        Assert.Equal(new DateOnly(2026, 11, 9), QuickCapture.Due(CaptureDue.NextWeek, Thursday));
        Assert.Equal(new DateOnly(2026, 11, 16), QuickCapture.Due(CaptureDue.NextWeek, new DateOnly(2026, 11, 9)));
        Assert.Null(QuickCapture.Due(CaptureDue.NoDay, Thursday));
    }

    [Theory]
    [InlineData("9", 9, 0)]
    [InlineData("9:30", 9, 30)]
    [InlineData("09h30", 9, 30)]
    [InlineData("14:00", 14, 0)]
    [InlineData("2pm", 14, 0)]
    [InlineData("12 am", 0, 0)]
    [InlineData("2:30 PM", 14, 30)]
    public void Times_are_read_the_ways_people_type_them(string typed, int hour, int minute) =>
        Assert.Equal(new TimeOnly(hour, minute), QuickCapture.Time(typed));

    [Theory]
    [InlineData("25:00")]
    [InlineData("9:7")]
    [InlineData("13pm")]
    [InlineData("soon")]
    public void Nonsense_times_are_refused(string typed) => Assert.Null(QuickCapture.Time(typed));

    [Fact]
    public void A_quick_event_has_a_day_and_an_optional_time_or_range()
    {
        var (allDay, _) = QuickCapture.Event("Bosveld site walk-through", Thursday, "", "e1");
        Assert.Equal((Thursday, (TimeOnly?)null), (allDay!.Date, allDay.Start));

        var (meeting, _) = QuickCapture.Event("Call with Lerato\nAbout the booking page", Thursday, "9:00-10:30", "e2");
        Assert.Equal((new TimeOnly(9, 0), new TimeOnly(10, 30), "About the booking page"), (meeting!.Start, meeting.End, meeting.Notes));

        Assert.NotNull(QuickCapture.Event("Call", Thursday, "3pm to 2pm", "e3").Problem);
        Assert.NotNull(QuickCapture.Event("Call", Thursday, "after lunch", "e4").Problem);
        Assert.NotNull(QuickCapture.Event("Call", null, "", "e5").Problem);
        Assert.NotNull(QuickCapture.Event("", Thursday, "", "e6").Problem);
    }

    [Fact]
    public void A_shortcut_can_not_take_keys_another_one_has()
    {
        var shots = new ScreenshotSettings();
        var (keys, _) = Hotkey.Parse("Ctrl+Alt+S");
        Assert.Equal(GlobalShortcut.Screenshot, GlobalShortcuts.TakenBy(keys!, shots, "Ctrl+Alt+N", GlobalShortcut.ColourPicker));
        Assert.Null(GlobalShortcuts.TakenBy(keys!, shots, "Ctrl+Alt+N", GlobalShortcut.Screenshot));
        var (colour, _) = Hotkey.Parse("Ctrl+Alt+C");
        Assert.Equal(GlobalShortcut.ColourPicker, GlobalShortcuts.TakenBy(colour!, shots, "Ctrl+Alt+N", GlobalShortcut.QuickNote));
        Assert.Equal("That's the colour picker shortcut already. Pick other keys for the quick note.",
            GlobalShortcuts.Clash(GlobalShortcut.ColourPicker, GlobalShortcut.QuickNote));
    }

    [Fact]
    public void The_clipboard_history_shortcut_is_checked_against_the_others()
    {
        var shots = new ScreenshotSettings();
        var (keys, _) = Hotkey.Parse("Ctrl+Alt+V");
        Assert.Equal(GlobalShortcut.ClipboardHistory, GlobalShortcuts.TakenBy(keys!, shots, "Ctrl+Alt+N", GlobalShortcut.Screenshot));
        Assert.Null(GlobalShortcuts.TakenBy(keys!, shots, "Ctrl+Alt+N", GlobalShortcut.ClipboardHistory));
    }
}
