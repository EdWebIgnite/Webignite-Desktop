using System.Globalization;

namespace Webignite.Core.Finance;

public enum ExpectedOutcome
{
    /// <summary>Still hoped for.</summary>
    Open,

    /// <summary>The money came in.</summary>
    CameIn,

    /// <summary>It fell through.</summary>
    Lost,
}

/// <summary>
/// Money that may come in, typed by hand: who from, what for, how much, in which month and how sure. For work not
/// yet quoted or invoiced as much as for work that is. Kept on the PC it was typed on, never synced.
/// </summary>
public sealed record ExpectedIncome
{
    public required string Id { get; init; }
    public string Who { get; init; } = "";
    public string What { get; init; } = "";

    /// <summary>The whole amount hoped for, in Rand.</summary>
    public decimal Amount { get; init; }

    /// <summary>The month it is expected in: the first day of that month.</summary>
    public DateOnly Month { get; init; }

    /// <summary>How sure, 0 to 100.</summary>
    public int Percent { get; init; } = 50;

    public ExpectedOutcome Outcome { get; init; }

    /// <summary>The day it was ticked off as come in or lost.</summary>
    public DateOnly? Settled { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

/// <summary>A month of expected income: everything hoped for, and what it is likely worth.</summary>
public sealed record ExpectedMonth(DateOnly Month, IReadOnlyList<ExpectedIncome> Items, decimal Possible, decimal Likely);

public static class ExpectedRules
{
    /// <summary>The labels offered when adding, each with the percentage it stands for.</summary>
    public static readonly IReadOnlyList<(string Label, int Percent)> Presets = [("Confirmed", 100), ("Likely", 75), ("Maybe", 50), ("Long shot", 25)];

    /// <summary>The word for a percentage: Confirmed from 90, Likely from 60, Maybe from 35, else Long shot.</summary>
    public static string Label(int percent) => percent switch
    {
        >= 90 => "Confirmed",
        >= 60 => "Likely",
        >= 35 => "Maybe",
        _ => "Long shot",
    };

    /// <summary>What a line is likely worth: its amount by how sure it is.</summary>
    public static decimal Likely(ExpectedIncome item) => Documents.Money.Cents(item.Amount * Math.Clamp(item.Percent, 0, 100) / 100m);

    /// <summary>A percentage as typed ("60", "60%"), or null when it isn't a whole number from 0 to 100.</summary>
    public static int? ParsePercent(string? typed) =>
        int.TryParse((typed ?? "").Trim().TrimEnd('%').Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var percent) && percent <= 100 ? percent : null;

    /// <summary>Why a line can't be kept, or null when it can.</summary>
    public static string? Problem(ExpectedIncome item)
    {
        if (string.IsNullOrWhiteSpace(item.Who) && string.IsNullOrWhiteSpace(item.What))
            return "Say who it is from or what it is for.";
        if (item.Amount <= 0)
            return "Type the amount in Rand, e.g. 12500.";
        return null;
    }

    /// <summary>What is still hoped for, month by month from the earliest, each with its total and its likely total.</summary>
    public static IReadOnlyList<ExpectedMonth> Months(IEnumerable<ExpectedIncome> items) =>
    [
        .. items.Where(i => i.Outcome == ExpectedOutcome.Open)
            .GroupBy(i => new DateOnly(i.Month.Year, i.Month.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var lines = g.OrderByDescending(i => i.Percent).ThenByDescending(i => i.Amount).ToList();
                return new ExpectedMonth(g.Key, lines, lines.Sum(i => i.Amount), lines.Sum(Likely));
            }),
    ];

    /// <summary>What was ticked off, the newest first: came in or lost.</summary>
    public static IReadOnlyList<ExpectedIncome> Settled(IEnumerable<ExpectedIncome> items) =>
        [.. items.Where(i => i.Outcome != ExpectedOutcome.Open).OrderByDescending(i => i.Settled ?? DateOnly.MinValue).ThenByDescending(i => i.Month)];

    /// <summary>The list with one line added, or changed when it is there already.</summary>
    public static IReadOnlyList<ExpectedIncome> Put(IEnumerable<ExpectedIncome> items, ExpectedIncome item)
    {
        var list = items.ToList();
        var at = list.FindIndex(i => i.Id == item.Id);
        if (at < 0)
            list.Add(item);
        else
            list[at] = item;
        return list;
    }

    /// <summary>The list with one line ticked off as come in or lost, or put back as still hoped for.</summary>
    public static IReadOnlyList<ExpectedIncome> Settle(IEnumerable<ExpectedIncome> items, string id, ExpectedOutcome outcome, DateOnly today) =>
        [.. items.Select(i => i.Id == id ? i with { Outcome = outcome, Settled = outcome == ExpectedOutcome.Open ? null : today } : i)];

    public static IReadOnlyList<ExpectedIncome> Remove(IEnumerable<ExpectedIncome> items, string id) => [.. items.Where(i => i.Id != id)];

    /// <summary>The months offered when adding: this one and the next <paramref name="ahead"/>.</summary>
    public static IReadOnlyList<DateOnly> MonthChoices(DateOnly today, int ahead = 18) =>
        [.. Enumerable.Range(0, ahead + 1).Select(n => new DateOnly(today.Year, today.Month, 1).AddMonths(n))];
}
