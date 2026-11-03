using System.Globalization;
using Webignite.Core.Clients;
using Webignite.Core.Documents;

namespace Webignite.Core.Time;

/// <summary>
/// A draft invoice from a client's logged time for one month: a line per project (and one for time on no
/// project), hours to two decimals at the rate that applies. Time with no rate can't be priced, so it is left
/// out and said so.
/// </summary>
public static class TimeInvoice
{
    /// <summary>The lines, and how much time had no rate and was left out.</summary>
    public static (IReadOnlyList<Line> Lines, int Unpriced) Lines(IEnumerable<TimeEntry> entries, Client client, DateOnly month)
    {
        var start = new DateOnly(month.Year, month.Month, 1);
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
            lines.Add(new Line
            {
                Description = $"{what}, {monthName} ({TimeRules.Format(minutes)})",
                Quantity = Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero),
                UnitPrice = rate,
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
            Reference = "Time, " + new DateOnly(month.Year, month.Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            Lines = lines,
        }, settings, today);
    }

    private static string ProjectName(Client client, string? id) => id is null ? "" : client.Projects.FirstOrDefault(p => p.Id == id)?.Name ?? "";
}
