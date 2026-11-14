using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Projects;
using Webignite.Core.Renewals;
using Webignite.Core.Studio;
using Webignite.Core.Web;

namespace Webignite.Tests;

public sealed class StudioMapTests
{
    private static readonly DateOnly Today = new(2026, 11, 14);

    private static Client NewClient(string id, string name, string? site = null) =>
        new() { Id = id, Name = name, Website = site };

    private static StoredDocument Invoice(string client, decimal total, DateOnly on, DocumentStatus status = DocumentStatus.InXero) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Status = status,
        Document = new Document
        {
            Kind = DocumentKind.Invoice, Origin = DocumentOrigin.New, Customer = new Customer { Name = client }, Date = on, Total = total, Lines = [],
        },
    };

    private static IReadOnlyList<Star> Build(IEnumerable<Client> clients, IEnumerable<Project>? projects = null, IEnumerable<ProjectTask>? tasks = null,
        IEnumerable<StoredDocument>? documents = null, UptimeSettings? uptime = null, IEnumerable<Renewal>? renewals = null, bool demo = false) =>
        StudioMap.Build(clients, projects ?? [], tasks ?? [], documents ?? [], uptime ?? new UptimeSettings(), renewals ?? [], Today, demo);

    [Fact]
    public void Every_client_is_a_dot_with_its_projects_sites_and_renewals_around_it()
    {
        var bosveld = NewClient("c1", "Bosveld Builders", "https://bosveld.co.za");
        var stars = Build([bosveld, NewClient("c2", "Fynbos Physio")],
            projects: [new Project { Id = "p1", Name = "New website", ClientId = "c1" }, new Project { Id = "p2", Name = "Old job", ClientId = "c1", Status = ProjectStatus.Done }],
            tasks: [new ProjectTask { Id = "t1", ProjectId = "p1", Title = "Wireframes" }],
            renewals: [new Renewal { Id = "r1", Name = "bosveld.co.za", ClientName = "bosveld builders", RenewsOn = new DateOnly(2027, 3, 1) }]);

        Assert.Equal(["Bosveld Builders", "Fynbos Physio"], stars.Where(s => s.ParentId is null).Select(s => s.Name));
        var around = stars.Where(s => s.ParentId == "c1").ToList();
        // A finished project is left off.
        Assert.Equal([StarKind.Project, StarKind.Site, StarKind.Domain], around.Select(s => s.Kind));
        Assert.Equal("1 task open", around[0].Detail);
        Assert.Equal("Renews 1 Mar 2027", around[2].Detail);
        Assert.Equal("1 project, 1 website, 1 renewal", stars[0].Detail);
        Assert.Equal(StarState.Busy, stars[0].State);
        Assert.Equal("Nothing on the go", stars.Single(s => s.Id == "c2").Detail);
    }

    [Fact]
    public void A_client_is_bigger_the_more_was_invoiced_in_the_last_year()
    {
        var stars = Build([NewClient("c1", "Bosveld Builders"), NewClient("c2", "Fynbos Physio")], documents:
        [
            Invoice("Bosveld Builders", 12500m, Today.AddMonths(-2)),
            Invoice("Bosveld Builders", 9000m, Today.AddMonths(-2), DocumentStatus.Draft),
            Invoice("Fynbos Physio", 40000m, Today.AddYears(-2)),
        ]);

        // Drafts and invoices older than a year don't count.
        Assert.Equal(13.5, stars.Single(s => s.Id == "c1").Weight);
        Assert.Equal(1, stars.Single(s => s.Id == "c2").Weight);
        Assert.Equal($"{Money.Rand(12500m)} invoiced this year", stars.Single(s => s.Id == "c1").Detail);
    }

    [Fact]
    public void A_site_that_is_down_marks_its_client_as_a_problem()
    {
        var uptime = new UptimeSettings
        {
            Sites = new Dictionary<string, WatchedSite> { ["bosveld.co.za"] = new() { Domain = "bosveld.co.za", DownSince = DateTimeOffset.UtcNow } },
        };
        var stars = Build([NewClient("c1", "Bosveld Builders", "https://bosveld.co.za")], uptime: uptime);

        Assert.Equal(StarState.Problem, stars.Single(s => s.Kind == StarKind.Site).State);
        Assert.Equal(StarState.Problem, stars.Single(s => s.Id == "c1").State);
    }

    [Fact]
    public void Projects_for_no_client_hang_off_the_studios_own_dot()
    {
        var stars = Build([], projects: [new Project { Id = "p1", Name = "App fixes and features" }]);

        Assert.Equal(StudioMap.OwnId, stars[0].Id);
        Assert.Equal(StudioMap.OwnId, stars[1].ParentId);
    }

    [Fact]
    public void Demo_names_hide_who_the_clients_are_and_what_they_pay()
    {
        var stars = Build([NewClient("c1", "Bosveld Builders", "https://bosveld.co.za")],
            projects: [new Project { Id = "p1", Name = "Bosveld Builders new website", ClientId = "c1" }],
            documents: [Invoice("Bosveld Builders", 12500m, Today.AddDays(-3))], demo: true);

        Assert.DoesNotContain(stars, s => s.Name.Contains("Bosveld", StringComparison.OrdinalIgnoreCase) || s.Detail.Contains("R ", StringComparison.Ordinal));
        Assert.Equal("Marula Coffee Roasters", stars[0].Name);
        Assert.Equal("marulacoffeeroasters.co.za", stars.Single(s => s.Kind == StarKind.Site).Name);
        // The size is still the real one, so the map keeps its shape.
        Assert.True(stars[0].Weight > 13);
    }

    [Fact]
    public void The_layout_fits_the_square_puts_the_heaviest_in_the_middle_and_overlaps_no_clients()
    {
        var clients = Enumerable.Range(1, 30).Select(i => new Star("c" + i, StarKind.Client, "Client " + i, null, i, StarState.Quiet, "")).ToList();
        var around = Enumerable.Range(1, 14).Select(i => new Star("p" + i, StarKind.Project, "Project " + i, "c30", 1, StarState.Quiet, ""));
        var placed = StudioMap.Layout([.. clients, .. around]);

        Assert.Equal(44, placed.Count);
        Assert.All(placed, p => Assert.True(Math.Abs(p.X) + p.Radius <= 1.0001 && Math.Abs(p.Y) + p.Radius <= 1.0001));
        var middle = placed.Single(p => p.Star.Id == "c30");
        Assert.Equal(0, middle.X, 6);
        Assert.Equal(0, middle.Y, 6);
        var dots = placed.Where(p => p.Star.ParentId is null).ToList();
        foreach (var a in dots)
            foreach (var b in dots.Where(b => b != a))
                Assert.True(Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)) > a.Radius + b.Radius, $"{a.Star.Name} overlaps {b.Star.Name}");
        // The same dots land in the same places every time.
        Assert.Equal(placed, StudioMap.Layout([.. clients, .. around]));
    }

    [Fact]
    public void A_point_finds_the_dot_under_it_and_what_circles_a_client_before_the_client()
    {
        var placed = new[]
        {
            new PlacedStar(new Star("c", StarKind.Client, "Client", null, 1, StarState.Quiet, ""), 0, 0, 0.3),
            new PlacedStar(new Star("p", StarKind.Project, "Project", "c", 1, StarState.Quiet, ""), 0.25, 0, 0.05),
        };

        Assert.Equal("p", StudioMap.At(placed, 0.26, 0)!.Star.Id);
        Assert.Equal("c", StudioMap.At(placed, -0.1, 0.1)!.Star.Id);
        Assert.Null(StudioMap.At(placed, 0.9, 0.9));
    }
}
