using Webignite.Core.Layout;

namespace Webignite.Tests;

/// <summary>Calendar and Planner sized by dragging, kept within sensible bounds.</summary>
public sealed class CalendarSizesTests
{
    [Fact]
    public void A_month_stays_readable_and_within_the_window()
    {
        Assert.Equal(CalendarSizing.LeastMonth, CalendarSizing.Month(100, 900));
        Assert.Equal(600, CalendarSizing.Month(600, 900));
        Assert.Equal(900, CalendarSizing.Month(2000, 900));
        Assert.Equal(CalendarSizing.LeastMonth, CalendarSizing.Month(500, 200));
    }

    [Fact]
    public void The_day_panel_leaves_the_month_at_least_half_the_width()
    {
        Assert.Equal(CalendarSizing.LeastSide, CalendarSizing.Side(50, 1600));
        Assert.Equal(400, CalendarSizing.Side(400, 1600));
        Assert.Equal(500, CalendarSizing.Side(700, 1000));
        Assert.Equal(CalendarSizing.MostSide, CalendarSizing.Side(900, 3000));
    }

    [Fact]
    public void Planner_days_stay_between_short_and_tall()
    {
        Assert.Equal(CalendarSizing.LeastPlannerDay, CalendarSizing.PlannerDay(20));
        Assert.Equal(320, CalendarSizing.PlannerDay(320));
        Assert.Equal(CalendarSizing.MostPlannerDay, CalendarSizing.PlannerDay(5000));
    }
}
