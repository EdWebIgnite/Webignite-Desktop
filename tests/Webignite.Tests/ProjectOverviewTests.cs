using Webignite.Core.Projects;

namespace Webignite.Tests;

public class ProjectOverviewTests
{
    private static readonly Stamp At = new("me", new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));

    private static Project Bosveld() => new() { Id = "p1", Name = "Bosveld Builders website" };

    [Theory]
    [InlineData("bosveld.co.za", "https://bosveld.co.za")]
    [InlineData("  https://staging.bosveld.co.za/brief ", "https://staging.bosveld.co.za/brief")]
    [InlineData("http://localhost:5000", "http://localhost:5000")]
    [InlineData("bosveld", null)]
    [InlineData("mailto:pieter@bosveld.co.za", null)]
    [InlineData("", null)]
    public void A_web_address_is_made_whole_or_turned_down(string typed, string? expected) =>
        Assert.Equal(expected, ProjectOverview.Address(typed));

    [Fact]
    public void A_key_resource_takes_the_sites_name_when_it_has_no_title()
    {
        var (changed, problem) = ProjectOverview.AddLink(Bosveld(), "", "www.figma.com/file/abc", "l1");
        Assert.Equal("", problem);
        var link = Assert.Single(changed!.Links);
        Assert.Equal(("figma.com", "https://www.figma.com/file/abc"), (link.Title, link.Url));

        var (named, _) = ProjectOverview.AddLink(changed, "Brief", "bosveld.co.za/brief", "l2");
        Assert.Equal(["figma.com", "Brief"], named!.Links.Select(l => l.Title));
        Assert.Equal(["Brief"], ProjectOverview.RemoveLink(named, "l1").Links.Select(l => l.Title));

        Assert.Null(ProjectOverview.AddLink(Bosveld(), "Notes", "just words", "l3").Changed);
    }

    [Fact]
    public void A_status_update_goes_first_and_sets_the_projects_status()
    {
        var (first, _) = ProjectOverview.Post(Bosveld(), ProjectHealth.OnTrack, " Home page signed off. ", At, "u1");
        var (second, _) = ProjectOverview.Post(first!, ProjectHealth.AtRisk, "Waiting on photos from Pieter.", At with { At = At.At.AddDays(7) }, "u2");
        Assert.Equal(ProjectHealth.AtRisk, second!.Health);
        Assert.Equal(["u2", "u1"], second.Updates.Select(u => u.Id));
        Assert.Equal("Home page signed off.", second.Updates[1].Text);
        Assert.Equal("me", second.Updates[0].By);

        var (none, problem) = ProjectOverview.Post(Bosveld(), ProjectHealth.None, "Fine", At, "u3");
        Assert.Null(none);
        Assert.Contains("on track", problem);
    }

    [Fact]
    public void Milestones_come_soonest_first_and_progress_counts_tasks_not_subtasks()
    {
        var today = new DateOnly(2026, 11, 6);
        ProjectTask Task(string title, bool milestone = false, DateOnly? due = null, bool done = false, string? parent = null, double order = 0) =>
            new() { Id = title, ProjectId = "p1", Title = title, Milestone = milestone, Due = due, Done = done, ParentId = parent, Order = order };
        var tasks = new[]
        {
            Task("Launch", milestone: true, due: today.AddDays(20)),
            Task("Sign-off", milestone: true, due: today.AddDays(5)),
            Task("Handover", milestone: true, order: 3),
            Task("Wireframes", done: true),
            Task("Photos", due: today.AddDays(-2)),
            Task("Mobile wireframe", parent: "Wireframes"),
        };
        Assert.Equal(["Sign-off", "Launch", "Handover"], ProjectOverview.Milestones(tasks).Select(t => t.Title));

        var progress = ProjectOverview.Progress(tasks, today);
        Assert.Equal((1, 4, 1, 5), (progress.Done, progress.Open, progress.Late, progress.Total));
        Assert.Equal(0.2, progress.Share, 3);
        Assert.Equal(0, ProjectOverview.Progress([], today).Share);
    }

    [Fact]
    public void A_project_description_keeps_its_formatting_only_when_it_has_some()
    {
        var plain = ProjectOverview.WithDescription(Bosveld(), Webignite.Core.Writing.RichText.FromText("Five pages and a contact form."));
        Assert.Equal(("Five pages and a contact form.", ""), (plain.Notes, plain.NotesHtml));
        Assert.Equal("Five pages and a contact form.", Webignite.Core.Writing.RichText.ToText(ProjectOverview.Description(plain)).TrimEnd());
        var cleared = ProjectOverview.WithDescription(plain, Webignite.Core.Writing.RichDocument.Empty());
        Assert.Equal(("", ""), (cleared.Notes, cleared.NotesHtml));
    }
}
