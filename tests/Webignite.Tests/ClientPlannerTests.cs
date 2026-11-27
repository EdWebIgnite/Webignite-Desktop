using Webignite.Core.Clients;
using Webignite.Core.Projects;

namespace Webignite.Tests;

public sealed class ClientPlannerTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);

    private static Project P(string name, string? client, ProjectStatus status = ProjectStatus.Active, DateOnly? due = null, bool deleted = false) => new()
    {
        Id = name, Name = name, ClientId = client, Status = status, Due = due,
        Deleted = deleted ? new Stamp("me", DateTimeOffset.UnixEpoch) : null,
    };

    [Fact]
    public void A_clients_Planner_projects_show_the_ones_still_going_first_and_the_soonest_due_at_the_top()
    {
        var all = new[]
        {
            P("Done site", "c1", ProjectStatus.Done),
            P("SEO", "c1"),
            P("Shop", "c1", due: Today.AddDays(10)),
            P("Gone", "c1", deleted: true),
            P("Logo", "c1", ProjectStatus.OnHold),
            P("Someone else", "c2"),
            P("Booking page", "c1", due: Today.AddDays(-2)),
        };

        Assert.Equal(["Booking page", "Shop", "SEO", "Logo", "Done site"], ClientPlanner.For(all, "c1").Select(p => p.Name));
    }

    [Fact]
    public void Each_project_says_where_it_stands()
    {
        string Day(DateOnly d) => d.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("Due 25 Nov, late", ClientPlanner.Line(P("a", "c", due: Today.AddDays(-2)), Today, Day));
        Assert.Equal("Due 7 Dec", ClientPlanner.Line(P("a", "c", due: Today.AddDays(10)), Today, Day));
        Assert.Equal("Under way", ClientPlanner.Line(P("a", "c"), Today, Day));
        Assert.Equal("On hold", ClientPlanner.Line(P("a", "c", ProjectStatus.OnHold, Today.AddDays(-2)), Today, Day));
        Assert.Equal("Done", ClientPlanner.Line(P("a", "c", ProjectStatus.Done), Today, Day));
        Assert.True(ClientPlanner.IsLate(P("a", "c", due: Today.AddDays(-1)), Today));
        Assert.False(ClientPlanner.IsLate(P("a", "c", ProjectStatus.Done, Today.AddDays(-1)), Today));
    }
}
