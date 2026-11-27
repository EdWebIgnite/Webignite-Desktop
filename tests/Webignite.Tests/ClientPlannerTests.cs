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

public sealed class ClientProjectsMergeTests
{
    private static readonly Client Studio = new()
    {
        Id = "studio", Name = "Studio Nine",
        Projects =
        [
            new ClientProject { Id = "old1", Name = "New website", HourlyRate = 650m },
            new ClientProject { Id = "old2", Name = "Logo refresh", Finished = true },
            new ClientProject { Id = "gone", Name = "Banner ads" },
        ],
    };

    [Fact]
    public void Projects_on_the_old_client_list_become_Planner_projects_with_the_same_id_once()
    {
        var deleted = new Project { Id = "gone", Name = "Banner ads", ClientId = "studio", Deleted = new Stamp("me", DateTimeOffset.UnixEpoch) };
        var merged = ClientPlanner.ToMerge([Studio], [deleted]);

        Assert.Equal(["old1", "old2"], merged.Select(p => p.Id));
        Assert.Equal(650m, merged[0].HourlyRate);
        Assert.True(merged.All(p => p.ClientId == "studio" && p.Shared));
        Assert.Equal(ProjectStatus.Done, merged[1].Status);
        Assert.Empty(ClientPlanner.ToMerge([Studio], [.. merged, deleted]));
    }

    [Fact]
    public void Time_sees_the_Planner_projects_with_who_they_are_for()
    {
        var planner = new[]
        {
            new Project { Id = "old1", Name = "New website", ClientId = "studio", For = "Karoo Coffee", HourlyRate = 700m },
            new Project { Id = "p2", Name = "Shop", ClientId = "studio", Status = ProjectStatus.Done },
            new Project { Id = "p3", Name = "Elsewhere", ClientId = "other" },
        };

        var time = ClientPlanner.ForTime(Studio, planner);

        Assert.Equal(["old1", "p2", "old2", "gone"], time.Select(p => p.Id));
        Assert.Equal("New website for Karoo Coffee", time[0].Name);
        Assert.Equal(700m, time[0].HourlyRate);
        Assert.True(time[1].Finished);
    }
}
