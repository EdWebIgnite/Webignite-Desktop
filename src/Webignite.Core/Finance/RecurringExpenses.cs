using System.Globalization;
namespace Webignite.Core.Finance;

public static class RecurringExpenses
{
    public static string Key(RecurringExpense recurring, DateOnly month) => string.Create(CultureInfo.InvariantCulture, $"recurring:{recurring.Id}:{month:yyyy-MM}");

    /// <summary>The day it goes off in a given month. Day 31 lands on the 30th in November, the 28th or 29th in February.</summary>
    public static DateOnly DateIn(RecurringExpense recurring, DateOnly month) => MonthlySchedule.DateIn(recurring.DayOfMonth, month);

    /// <summary>
    /// The expenses that monthly items call for up to today and haven't been added yet.
    /// <paramref name="existingKeys"/> holds every key ever added, including ones that were deleted,
    /// so removing an entry by hand doesn't bring it back.
    /// </summary>
    public static IReadOnlyList<MoneyEntry> Due(IEnumerable<RecurringExpense> recurring, IReadOnlySet<string> existingKeys,
        DateOnly today, DateTimeOffset now)
    {
        var due = new List<MoneyEntry>();
        foreach (var item in recurring.Where(r => !r.Paused))
        {
            foreach (var month in MonthlySchedule.MonthsSoFar(item.Starts, item.Ends, today))
            {
                var date = DateIn(item, month);
                var key = Key(item, month);
                if (date > today || existingKeys.Contains(key))
                    continue;
                due.Add(new MoneyEntry
                {
                    Id = MoneyEntry.NewId(),
                    Kind = EntryKind.Expense,
                    Date = date,
                    Amount = item.Amount,
                    Party = item.Name,
                    Category = item.Category,
                    RecurringKey = key,
                    Created = now,
                });
            }
        }
        return due;
    }

    /// <summary>
    /// Pausing and resuming. Resuming starts again from this month, so the months it was paused
    /// aren't filled in afterwards.
    /// </summary>
    public static RecurringExpense SetPaused(RecurringExpense recurring, bool paused, DateOnly today) => paused
        ? recurring with { Paused = true }
        : recurring with { Paused = false, Starts = MonthlySchedule.ResumeFrom(recurring.Starts, today) };

    /// <summary>Monthly cost of everything that is running, for "R x a month" summaries.</summary>
    public static decimal MonthlyTotal(IEnumerable<RecurringExpense> recurring, DateOnly today) =>
        recurring.Where(r => MonthlySchedule.IsRunning(r.Paused, r.Ends, today)).Sum(r => r.Amount);

    public static IReadOnlyList<string> Check(RecurringExpense recurring)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(recurring.Name))
            problems.Add("Give it a name, like xneelo server or Adobe.");
        if (recurring.Amount <= 0)
            problems.Add("Type an amount more than R 0.");
        if (recurring.DayOfMonth is < 1 or > 31)
            problems.Add("The day of the month is 1 to 31.");
        if (MonthlySchedule.EndsBeforeStart(recurring.Starts, recurring.Ends))
            problems.Add("It can't end before it starts.");
        return problems;
    }
}
