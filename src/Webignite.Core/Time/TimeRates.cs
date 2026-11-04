using Webignite.Core.Clients;
using Webignite.Core.Documents;

namespace Webignite.Core.Time;

/// <summary>Time in Rand: a rate per client, which a project can override.</summary>
public static partial class TimeRules
{
    /// <summary>The rate an entry is charged at: its project's own rate, else its client's. Null when neither is set.</summary>
    public static decimal? Rate(TimeEntry entry, IReadOnlyList<Client> clients)
    {
        if (entry.ClientId is null || clients.FirstOrDefault(c => c.Id == entry.ClientId) is not { } client)
            return null;
        var project = entry.ProjectId is null ? null : client.Projects.FirstOrDefault(p => p.Id == entry.ProjectId);
        return project?.HourlyRate ?? client.HourlyRate;
    }

    /// <summary>Minutes at an hourly rate, to the cent.</summary>
    public static decimal Value(int minutes, decimal rate) => Money.Cents(minutes / 60m * rate);

    /// <summary>
    /// What the entries come to at their rates, and the minutes with no rate to price them. Value is null when
    /// none of the time has a rate.
    /// </summary>
    public static (decimal? Value, int Unpriced) Worth(IEnumerable<TimeEntry> entries, IReadOnlyList<Client> clients)
    {
        decimal total = 0;
        var priced = false;
        var unpriced = 0;
        foreach (var entry in entries)
        {
            if (Rate(entry, clients) is { } rate)
            {
                total += Value(entry.Minutes, rate);
                priced = true;
            }
            else
                unpriced += entry.Minutes;
        }
        return (priced ? total : null, unpriced);
    }

    /// <summary>
    /// A typed rate: "650", "R650", "R 650.00" or "650,50". Blank means no rate. Returns false when the text
    /// isn't an amount above nil.
    /// </summary>
    public static bool TryParseRate(string? text, out decimal? rate)
    {
        rate = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (Money.Parse(text.Replace("/h", "", StringComparison.OrdinalIgnoreCase)) is not { } value || value <= 0)
            return false;
        rate = Money.Cents(value);
        return true;
    }
}
