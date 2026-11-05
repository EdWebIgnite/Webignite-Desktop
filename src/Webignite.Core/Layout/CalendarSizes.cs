namespace Webignite.Core.Layout;

/// <summary>
/// How the person sized Calendar and Planner by dragging: the month's height (null fills the window), the width of
/// the day panel beside it, and how tall Planner's days are (null fits what's on them). Saved, so they stay so.
/// </summary>
public sealed record CalendarSizes
{
    public double? MonthHeight { get; init; }
    public double? SideWidth { get; init; }
    public double? PlannerDayHeight { get; init; }
}

public static class CalendarSizing
{
    /// <summary>The least room a month gets: enough for six weeks of readable days.</summary>
    public const double LeastMonth = 360;

    public const double LeastSide = 220;
    public const double MostSide = 560;
    public const double LeastPlannerDay = 140;
    public const double MostPlannerDay = 900;

    /// <summary>A month dragged to a height, kept between its least and the room on screen.</summary>
    public static double Month(double height, double room) => Math.Clamp(height, LeastMonth, Math.Max(LeastMonth, room));

    /// <summary>The day panel dragged to a width, kept readable and leaving the month at least as wide.</summary>
    public static double Side(double width, double room) => Math.Clamp(width, LeastSide, Math.Max(LeastSide, Math.Min(MostSide, room / 2)));

    public static double PlannerDay(double height) => Math.Clamp(height, LeastPlannerDay, MostPlannerDay);
}
