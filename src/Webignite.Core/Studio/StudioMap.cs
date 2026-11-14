using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Projects;
using Webignite.Core.Renewals;
using Webignite.Core.Web;

namespace Webignite.Core.Studio;

/// <summary>What a dot on the Studio map stands for.</summary>
public enum StarKind { Client, Project, Site, Domain }

/// <summary>How a dot shows: nothing going on, work under way, or something wrong (a site that is down).</summary>
public enum StarState { Quiet, Busy, Problem }

/// <summary>
/// One dot on the Studio map: a client, or something done for one (a project, a watched website, a domain or
/// hosting that renews), which circles its client. <paramref name="Weight"/> says how big a client is drawn.
/// </summary>
public sealed record Star(string Id, StarKind Kind, string Name, string? ParentId, double Weight, StarState State, string Detail);

/// <summary>A dot with its place: the middle and radius, where the whole map fits a square from -1 to 1.</summary>
public sealed record PlacedStar(Star Star, double X, double Y, double Radius);

/// <summary>
/// The Studio map: every client as a dot, bigger the more is done for them, with their projects, websites and
/// domains circling. What is on it and where is worked out here; the page only draws it.
/// </summary>
public static class StudioMap
{
    /// <summary>The dot that work for no client hangs off: the studio's own projects.</summary>
    public const string OwnId = "studio";

    /// <summary>
    /// The dots for everything the studio looks after. A client's weight is what was invoiced in the year up to
    /// <paramref name="today"/> (in thousands) plus a point for each thing done for them, so a client with no
    /// invoices yet still shows. With <paramref name="demo"/> every name is made up and no money is mentioned,
    /// for showing the map to people who shouldn't see who the clients are.
    /// </summary>
    public static IReadOnlyList<Star> Build(IEnumerable<Client> clients, IEnumerable<Project> projects, IEnumerable<ProjectTask> tasks,
        IEnumerable<StoredDocument> documents, UptimeSettings uptime, IEnumerable<Renewal> renewals, DateOnly today, bool demo = false,
        string studioName = "Our own work")
    {
        var all = clients.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var open = tasks.Where(t => t.Deleted is null && !t.Done && t.ParentId is null).GroupBy(t => t.ProjectId).ToDictionary(g => g.Key, g => g.Count());
        var live = projects.Where(p => p.Deleted is null && p.Status != ProjectStatus.Done).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var since = today.AddYears(-1);
        var invoiced = documents
            .Where(d => d.Document.Kind == DocumentKind.Invoice && d.Status == DocumentStatus.InXero && !Settlements.IsCancelled(d.Settled?.As)
                && d.Document.Date is { } on && on > since && on <= today)
            .GroupBy(d => ClientDirectory.Key(d.Document.Customer.Name))
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Document.Total ?? d.Document.LinesTotal()));
        var watched = Uptime.Domains(all, uptime);
        var due = renewals.ToList();

        var stars = new List<Star>();
        var number = 0;
        foreach (var client in all)
        {
            number++;
            var key = ClientDirectory.Key(client.Name);
            var name = demo ? StudioDemo.Client(number) : client.Name;
            var mine = new List<Star>();
            var at = 0;
            foreach (var project in live.Where(p => p.ClientId == client.Id))
            {
                var left = open.GetValueOrDefault(project.Id);
                mine.Add(new Star("project:" + project.Id, StarKind.Project, demo ? StudioDemo.Project(at++) : project.Name, client.Id, 1,
                    left > 0 ? StarState.Busy : StarState.Quiet, left == 0 ? "Nothing open" : left == 1 ? "1 task open" : $"{left} tasks open"));
            }
            at = 0;
            foreach (var site in watched.Where(w => w.Client is { } owner && ClientDirectory.Key(owner) == key))
            {
                var down = uptime.Sites.GetValueOrDefault(site.Domain)?.DownSince is not null;
                mine.Add(new Star("site:" + site.Domain, StarKind.Site, demo ? StudioDemo.Site(name, at++) : site.Domain, client.Id, 1,
                    down ? StarState.Problem : StarState.Quiet, down ? "Website is down" : "Website is up"));
            }
            at = 0;
            foreach (var renewal in due.Where(r => r.ClientName is { } owner && ClientDirectory.Key(owner) == key))
            {
                mine.Add(new Star("renewal:" + renewal.Id, StarKind.Domain, demo ? StudioDemo.Renewal(name, renewal.Kind, at++) : renewal.Name, client.Id, 1,
                    StarState.Quiet, $"Renews {renewal.RenewsOn.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}"));
            }
            var money = invoiced.GetValueOrDefault(key);
            var state = mine.Any(s => s.State == StarState.Problem) ? StarState.Problem : mine.Any(s => s.State == StarState.Busy) ? StarState.Busy : StarState.Quiet;
            stars.Add(new Star(client.Id, StarKind.Client, name, null, (double)(money / 1000m) + mine.Count + 1, state, Summary(mine, demo ? null : money)));
            stars.AddRange(mine);
        }

        // Projects for no client are the studio's own, and hang off a dot of its own.
        var own = live.Where(p => p.ClientId is null || all.All(c => c.Id != p.ClientId)).ToList();
        if (own.Count > 0)
        {
            var mine = own.Select((project, at) =>
            {
                var left = open.GetValueOrDefault(project.Id);
                return new Star("project:" + project.Id, StarKind.Project, demo ? StudioDemo.Project(at) : project.Name, OwnId, 1,
                    left > 0 ? StarState.Busy : StarState.Quiet, left == 0 ? "Nothing open" : left == 1 ? "1 task open" : $"{left} tasks open");
            }).ToList();
            stars.Add(new Star(OwnId, StarKind.Client, studioName, null, mine.Count + 1,
                mine.Any(s => s.State == StarState.Busy) ? StarState.Busy : StarState.Quiet, Summary(mine, null)));
            stars.AddRange(mine);
        }
        return stars;
    }

    /// <summary>"2 projects, 1 website, 3 renewals, R 12 500 invoiced this year"; "Nothing on the go" for a client with none of it.</summary>
    public static string Summary(IReadOnlyCollection<Star> around, decimal? invoiced)
    {
        var parts = new List<string>();
        Count(StarKind.Project, "project", "projects");
        Count(StarKind.Site, "website", "websites");
        Count(StarKind.Domain, "renewal", "renewals");
        if (invoiced is > 0)
            parts.Add($"{Money.Rand(invoiced.Value)} invoiced this year");
        return parts.Count == 0 ? "Nothing on the go" : string.Join(", ", parts);

        void Count(StarKind kind, string one, string many)
        {
            var n = around.Count(s => s.Kind == kind);
            if (n > 0)
                parts.Add(n == 1 ? $"1 {one}" : $"{n} {many}");
        }
    }

    private const double GoldenAngle = 2.39996322972865332;

    /// <summary>
    /// Where each dot goes. Clients wind out from the middle, heaviest first, the way seeds sit in a sunflower,
    /// so the biggest are in the middle and none overlap; what is done for a client circles it, in a second ring
    /// when there is a lot. The same dots always land in the same places. Everything fits a square from -1 to 1.
    /// </summary>
    public static IReadOnlyList<PlacedStar> Layout(IReadOnlyList<Star> stars)
    {
        var clients = stars.Where(s => s.ParentId is null).OrderByDescending(s => s.Weight).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
        if (clients.Count == 0)
            return [];
        var heaviest = Math.Max(1, clients.Max(c => c.Weight));
        var placed = new List<PlacedStar>(stars.Count);
        // Far enough apart that the widest client, with its ring, clears its neighbours.
        const double step = 0.62;
        for (var i = 0; i < clients.Count; i++)
        {
            var client = clients[i];
            var radius = 0.07 + 0.11 * Math.Sqrt(client.Weight / heaviest);
            var out_ = step * Math.Sqrt(i);
            var angle = i * GoldenAngle;
            var (x, y) = (out_ * Math.Cos(angle), out_ * Math.Sin(angle));
            placed.Add(new PlacedStar(client, x, y, radius));
            var around = stars.Where(s => s.ParentId == client.Id).ToList();
            const int ring = 10;
            for (var j = 0; j < around.Count; j++)
            {
                var inRing = Math.Min(ring, around.Count - j / ring * ring);
                var orbit = radius + 0.075 + 0.07 * (j / ring);
                var turn = angle + Math.Tau * (j % ring) / inRing + (j / ring) * 0.3;
                placed.Add(new PlacedStar(around[j], x + orbit * Math.Cos(turn), y + orbit * Math.Sin(turn), 0.026));
            }
        }
        var reach = placed.Max(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) + p.Radius);
        return reach <= 0 ? placed : [.. placed.Select(p => p with { X = p.X / reach, Y = p.Y / reach, Radius = p.Radius / reach })];
    }

    /// <summary>The dot under a point, the smallest first so something circling a client is picked over the client; null for empty sky.</summary>
    public static PlacedStar? At(IEnumerable<PlacedStar> placed, double x, double y, double slack = 0) =>
        placed.Where(p => Math.Pow(p.X - x, 2) + Math.Pow(p.Y - y, 2) <= Math.Pow(p.Radius + slack, 2)).OrderBy(p => p.Radius).FirstOrDefault();
}

/// <summary>Made-up names for showing the Studio pages to people who shouldn't see who the clients are.</summary>
public static class StudioDemo
{
    private static readonly string[] Clients =
    [
        "Marula Coffee Roasters", "Kudu Construction", "Aloe Physio", "Mzansi Motors", "Protea Legal", "Baobab Bikes", "Table Bay Dental",
        "Highveld Solar", "Blue Crane Travel", "Drakensberg Dairy", "Kalahari Kitchens", "Garden Route Guesthouse", "Rooibos Wellness",
        "Springbok Security", "Limpopo Logistics", "Overberg Optics", "Winelands Weddings", "Sunbird Studios", "Amathole Architects", "Vaal Vets",
    ];

    private static readonly string[] Projects =
        ["New website", "Brand refresh", "Online shop", "Monthly care plan", "Social media launch", "Logo and stationery", "Booking system", "Newsletter"];

    public static string Client(int number) =>
        number <= Clients.Length ? Clients[number - 1] : $"{Clients[(number - 1) % Clients.Length]} {(number - 1) / Clients.Length + 1}";

    public static string Project(int at) => Projects[at % Projects.Length];

    public static string Site(string client, int at) => at == 0 ? $"{Slug(client)}.co.za" : $"{Slug(client)}{at + 1}.co.za";

    public static string Renewal(string client, RenewalKind kind, int at) =>
        kind == RenewalKind.Domain ? Site(client, at) : $"Hosting for {client}";

    private static string Slug(string name) => new([.. name.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
}
