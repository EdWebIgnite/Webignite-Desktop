using Webignite.Core.Projects;

namespace Webignite.Core.Studio;

/// <summary>
/// A made-up studio for showing the Studio pages to people outside it: a full set of clients with projects,
/// websites, renewals and quotes, none of them real. It is the same every time, so a meeting can be rehearsed.
/// </summary>
public static class StudioDemo
{
    // Name, thousands invoiced this year, then what circles it: projects, websites, renewals, quotes out.
    private static readonly (string Name, int Invoiced, int Projects, int Sites, int Renewals, int Quotes)[] Clients =
    [
        ("Marula Coffee Roasters", 148, 3, 2, 3, 1), ("Kudu Construction", 96, 2, 1, 2, 0), ("Aloe Physio", 64, 1, 1, 2, 1),
        ("Mzansi Motors", 122, 2, 2, 2, 0), ("Protea Legal", 81, 1, 1, 1, 1), ("Baobab Bikes", 37, 2, 1, 1, 0),
        ("Table Bay Dental", 58, 1, 1, 2, 0), ("Highveld Solar", 110, 3, 1, 2, 1), ("Blue Crane Travel", 45, 1, 2, 2, 0),
        ("Drakensberg Dairy", 29, 1, 1, 1, 0), ("Kalahari Kitchens", 72, 2, 1, 1, 1), ("Garden Route Guesthouse", 33, 0, 1, 2, 0),
        ("Rooibos Wellness", 51, 1, 1, 1, 0), ("Springbok Security", 88, 1, 2, 2, 1), ("Limpopo Logistics", 24, 0, 1, 1, 0),
        ("Overberg Optics", 19, 1, 1, 1, 0), ("Winelands Weddings", 42, 2, 1, 1, 0), ("Sunbird Studios", 15, 0, 1, 1, 1),
        ("Amathole Architects", 67, 1, 1, 2, 0), ("Vaal Vets", 26, 1, 1, 1, 0),
    ];

    private static readonly string[] Projects =
        ["New website", "Brand refresh", "Online shop", "Monthly care plan", "Social media launch", "Logo and stationery", "Booking system", "Newsletter"];

    /// <summary>The website the made-up studio is shown having trouble with, so the map shows what watching looks like.</summary>
    public const string DownSite = "baobabbikes.co.za";

    /// <summary>The made-up studio's dots, as <see cref="StudioMap.Build"/> gives the real ones. No money is mentioned.</summary>
    public static IReadOnlyList<Star> Stars(DateOnly today)
    {
        var stars = new List<Star>();
        for (var c = 0; c < Clients.Length; c++)
        {
            var (name, invoicedThousands, projects, sites, renewals, quotes) = Clients[c];
            var id = "demo:" + c;
            var slug = Slug(name);
            var mine = new List<Star>();
            for (var i = 0; i < projects; i++)
            {
                var left = (c * 3 + i * 5) % 9;
                mine.Add(new Star($"{id}:project:{i}", StarKind.Project, Projects[(c + i * 3) % Projects.Length], id, 1,
                    left > 0 ? StarState.Busy : StarState.Quiet, left == 0 ? "Nothing open" : left == 1 ? "1 task open" : $"{left} tasks open"));
            }
            for (var i = 0; i < sites; i++)
            {
                var site = i == 0 ? $"{slug}.co.za" : $"shop.{slug}.co.za";
                var down = site == DownSite;
                mine.Add(new Star($"{id}:site:{i}", StarKind.Site, site, id, 1, down ? StarState.Problem : StarState.Quiet, down ? "Website is down" : "Website is up"));
            }
            for (var i = 0; i < renewals; i++)
            {
                var on = today.AddDays(20 + (c * 37 + i * 91) % 330);
                mine.Add(new Star($"{id}:renewal:{i}", StarKind.Domain, i == 0 ? $"{slug}.co.za" : i == 1 ? $"Hosting for {name}" : $"{slug}.com", id, 1,
                    StarState.Quiet, $"Renews {on.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}"));
            }
            for (var i = 0; i < quotes; i++)
                mine.Add(new Star($"{id}:quote:{i}", StarKind.Quote, $"Quote Q-{1040 + c}", id, 1, StarState.Busy, "Waiting for an answer"));
            stars.Add(new Star(id, StarKind.Client, name, null, invoicedThousands + mine.Count + 1, StudioMap.StateOf(mine), StudioMap.Summary(mine, null)));
            stars.AddRange(mine);
        }
        return stars;
    }

    private static readonly string[] Stops = ["Brief", "Design", "Build", "Content", "Launch"];
    private static readonly string[] Colours = ["Blue", "Aqua", "Purple", "Magenta", "YellowOrange", "BlueGreen", "Indigo", "HotPink", "Orange"];

    /// <summary>The made-up studio's projects as lines, at every stage from just briefed to nearly launched.</summary>
    public static IReadOnlyList<ProjectLine> Lines()
    {
        var lines = new List<ProjectLine>();
        var at = 0;
        for (var c = 0; c < Clients.Length && lines.Count < 12; c++)
        {
            for (var i = 0; i < Math.Min(1, Clients[c].Projects) && lines.Count < 12; i++)
            {
                // How many tasks each stop has, and how far the work has got, spread so no two lines look alike.
                var reached = (at * 7 + 3) % (Stops.Length * 4);
                var stops = new List<LineStop>();
                for (var s = 0; s < Stops.Length; s++)
                {
                    var total = 3 + (at + s * 2) % 4;
                    var done = Math.Clamp((int)Math.Round((reached / 4.0 - s) * total), 0, total);
                    stops.Add(new LineStop(Stops[s], done, total));
                }
                stops.Add(new LineStop(StudioLines.Finish, 0, 0));
                var finished = stops.Sum(s => s.Done);
                var all = stops.Sum(s => s.Total);
                lines.Add(new ProjectLine($"demo:line:{at}", Projects[(c + i * 3) % Projects.Length], Clients[c].Name, Colours[at % Colours.Length], stops,
                    StudioLines.PositionOf(stops), ProjectHealth.OnTrack, $"{finished} of {all} tasks done"));
                at++;
            }
        }
        return [.. lines.OrderByDescending(l => l.Position).ThenBy(l => l.Client, StringComparer.Ordinal)];
    }

    private static string Slug(string name) => new([.. name.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
}
