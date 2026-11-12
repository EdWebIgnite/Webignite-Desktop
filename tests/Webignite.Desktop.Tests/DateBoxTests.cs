using Webignite.Desktop.Controls;

namespace Webignite.Desktop.Tests;

/// <summary>The date box's own arithmetic: the weeks a month's page shows, and which days may be picked.</summary>
public sealed class DateBoxTests
{
    [Fact]
    public void A_months_page_is_six_weeks_from_the_week_its_first_day_is_in()
    {
        // November 2026 starts on a Sunday.
        var mondayFirst = DateBox.PageOf(new DateTime(2026, 11, 12), DayOfWeek.Monday);
        Assert.Equal(42, mondayFirst.Count);
        Assert.Equal(new DateTime(2026, 10, 26), mondayFirst[0]); // the Monday before the 1st
        Assert.Equal(new DateTime(2026, 11, 1), mondayFirst[6]);
        Assert.Equal(new DateTime(2026, 12, 6), mondayFirst[41]);
        Assert.All(mondayFirst.Where((_, i) => i % 7 == 0), day => Assert.Equal(DayOfWeek.Monday, day.DayOfWeek));

        var sundayFirst = DateBox.PageOf(new DateTime(2026, 11, 12), DayOfWeek.Sunday);
        Assert.Equal(new DateTime(2026, 11, 1), sundayFirst[0]); // the 1st is itself the week's first day
        Assert.All(sundayFirst.Where((_, i) => i % 7 == 0), day => Assert.Equal(DayOfWeek.Sunday, day.DayOfWeek));

        // Every day of the month is on its page, whichever day the week starts on.
        foreach (var starts in Enum.GetValues<DayOfWeek>())
        {
            var page = DateBox.PageOf(new DateTime(2027, 2, 1), starts);
            Assert.All(Enumerable.Range(1, 28), d => Assert.Contains(new DateTime(2027, 2, d), page));
        }
    }

    [Fact]
    public void A_day_outside_the_earliest_and_latest_cant_be_picked()
    {
        var day = new DateTime(2026, 11, 12, 15, 30, 0);
        Assert.True(DateBox.Allowed(day, null, null));
        Assert.True(DateBox.Allowed(day, new DateTime(2026, 11, 12, 23, 0, 0), null)); // the same day counts, whatever the time
        Assert.False(DateBox.Allowed(day, new DateTime(2026, 11, 13), null));
        Assert.True(DateBox.Allowed(day, null, new DateTime(2026, 11, 12)));
        Assert.False(DateBox.Allowed(day, null, new DateTime(2026, 11, 11)));
    }

    [Fact]
    public void What_the_box_writes_it_can_read_back()
    {
        var day = new DateTime(2026, 11, 2);
        Assert.Equal(day, DateBox.Read(DateBox.Written(day)));
        Assert.Null(DateBox.Read("not a date"));
        Assert.Null(DateBox.Read(""));
    }

    [Fact]
    public Task The_box_shows_the_day_and_what_follows_it_or_its_placeholder() => Ui.Run(() =>
    {
        var box = new DateBox { PlaceholderText = "Due" };
        var words = () => ((Avalonia.Controls.TextBlock)((Avalonia.Controls.DockPanel)box.Content!).Children[1]).Text;
        Assert.Equal("Due", words());

        box.SelectedDate = new DateTime(2026, 11, 2);
        Assert.Equal(DateBox.Written(new DateTime(2026, 11, 2)), words());

        box.Note = "14:00, every month";
        Assert.EndsWith(", 14:00, every month", words(), StringComparison.Ordinal);

        box.SelectedDate = null;
        Assert.Equal("Due", words());
    });
}
