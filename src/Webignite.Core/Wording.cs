using System.Globalization;

namespace Webignite.Core;

/// <summary>
/// Counts and days in words, written one way for the whole app: "1 day", "3 invoices", "yesterday",
/// "5 days ago", "Friday". Sentences are English, so month and weekday names are too.
/// </summary>
public static class Wording
{
    /// <summary>"1 invoice", "3 invoices". Nouns that don't just take an s pass their plural.</summary>
    public static string Count(int n, string noun, string? plural = null) => n == 1 ? $"1 {noun}" : $"{n} {plural ?? noun + "s"}";

    /// <summary>"1 day", "12 days".</summary>
    public static string Days(int days) => Count(days, "day");

    /// <summary>"today", "yesterday" or "5 days ago", for something that happened a number of days back.</summary>
    public static string Ago(int days) => days switch
    {
        <= 0 => "today",
        1 => "yesterday",
        _ => $"{days} days ago",
    };

    /// <summary>
    /// A day near today in words: "today", "tomorrow", "yesterday", "5 days ago", a weekday within the coming
    /// week, otherwise "14 Nov" (with the year when it isn't this year).
    /// </summary>
    public static string Day(DateOnly day, DateOnly today) => (day.DayNumber - today.DayNumber) switch
    {
        0 => "today",
        1 => "tomorrow",
        -1 => "yesterday",
        < 0 and var back => Ago(-back),
        < 7 => day.ToString("dddd", CultureInfo.InvariantCulture),
        _ => day.Year == today.Year
            ? day.ToString("d MMM", CultureInfo.InvariantCulture)
            : day.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
    };

    /// <summary>The first letter in capitals, for a word that starts a label: "Yesterday".</summary>
    public static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
