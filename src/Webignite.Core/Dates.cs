namespace Webignite.Core;

/// <summary>Weeks, months and tax years, worked out one way for the whole app.</summary>
public static class Dates
{
    /// <summary>The Monday of the week a day falls in. Weeks run Monday to Sunday.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>The first of the month a day falls in.</summary>
    public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>The South African tax year a day falls in: 1 March to the last day of February.</summary>
    public static (DateOnly From, DateOnly To) TaxYear(DateOnly date)
    {
        var start = new DateOnly(date.Month >= 3 ? date.Year : date.Year - 1, 3, 1);
        return (start, start.AddYears(1).AddDays(-1));
    }
}
