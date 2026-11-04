using System.Globalization;
using Webignite.Core.Clients;
using Webignite.Core.Documents;

namespace Webignite.Core.Time;

/// <summary>
/// A draft invoice from a client's logged time for one month: a line per project (and one for time on no
/// project), hours to two decimals, priced so each line comes to exactly what the minutes are worth at the rate
/// that applies. Time with no rate can't be priced, so it is left out and said so.
/// </summary>
public static class TimeInvoice
{
    /// <summary>The lines, and how much time had no rate and was left out.</summary>
    public static (IReadOnlyList<Line> Lines, int Unpriced) Lines(IEnumerable<TimeEntry> entries, Client client, DateOnly month)
    {
        var start = Webignite.Core.Dates.MonthStart(month);
        var theirs = entries.Where(e => e.ClientId == client.Id && e.Date >= start && e.Date < start.AddMonths(1)).ToList();
        var lines = new List<Line>();
        var unpriced = 0;
        var monthName = start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        foreach (var group in theirs.GroupBy(e => e.ProjectId).OrderBy(g => g.Key is null).ThenBy(g => ProjectName(client, g.Key)))
        {
            var minutes = group.Sum(e => e.Minutes);
            var project = group.Key is null ? null : client.Projects.FirstOrDefault(p => p.Id == group.Key);
            var rate = project?.HourlyRate ?? client.HourlyRate;
            if (rate is null)
            {
                unpriced += minutes;
                continue;
            }
            var what = project is null ? "Work" : project.Name;
            var hours = Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);
            lines.Add(new Line
            {
                Description = $"{what}, {monthName} ({TimeRules.Format(minutes)})",
                Quantity = hours,
                UnitPrice = PriceFor(hours, TimeRules.Value(minutes, rate.Value), rate.Value),
            });
        }
        return (lines, unpriced);
    }

    /// <summary>The draft invoice for the client, ready to open and check. Null when there is nothing to invoice.</summary>
    public static Document? Draft(IEnumerable<TimeEntry> entries, Client client, DateOnly month, DocumentSettings settings, DateOnly today)
    {
        var (lines, _) = Lines(entries, client, month);
        if (lines.Count == 0)
            return null;
        return DocumentDefaults.Complete(new Document
        {
            Kind = DocumentKind.Invoice,
            Origin = DocumentOrigin.New,
            Customer = new Customer { Name = client.Name, Email = client.Email },
            Reference = "Time, " + Webignite.Core.Dates.MonthStart(month).ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            Lines = lines,
        }, settings, today);
    }

    /// <summary>
    /// The unit price (to four decimals, as Xero takes it) that makes the rounded hours come to exactly what the
    /// minutes are worth, so the invoice and the Time page agree: 3.92 h of 3 h 55 min at R 750 is R 2 937.50.
    /// When the rounded hours already come out right, it is simply the rate.
    /// </summary>
    public static decimal PriceFor(decimal hours, decimal worth, decimal rate)
    {
        if (hours <= 0 || Money.Cents(hours * rate) == worth)
            return rate;
        var price = Math.Round(worth / hours, 4, MidpointRounding.AwayFromZero);
        for (var step = 0; step < 100 && Money.Cents(hours * price) != worth; step++)
            price += Money.Cents(hours * price) < worth ? 0.0001m : -0.0001m;
        return price;
    }

    private static string ProjectName(Client client, string? id) => id is null ? "" : client.Projects.FirstOrDefault(p => p.Id == id)?.Name ?? "";
}
