using Webignite.Core.Clients;
using Webignite.Core.Overview;

namespace Webignite.Tests;

public sealed class OnboardingTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    private static Client Bosveld => new() { Id = "c1", Name = "Bosveld Builders", ContactPerson = "Pieter van Wyk", Email = "pieter@bosveldbuilders.co.za" };

    [Fact]
    public void A_checklist_starts_with_the_usual_items()
    {
        var client = Onboarding.Start(Bosveld, Today);
        Assert.Equal(Onboarding.Standard.Count, client.Onboarding.Count);
        Assert.True(Onboarding.InProgress(client));
        Assert.Equal($"0 of {Onboarding.Standard.Count} in", Onboarding.Progress(client));
        Assert.Equal(Today, client.OnboardingStarted);
    }

    [Fact]
    public void Ticking_records_the_day_and_unticking_clears_it()
    {
        var client = Onboarding.Start(Bosveld, Today.AddDays(-10));
        client = Onboarding.Tick(client, "Photos", true, Today);
        Assert.Equal(Today, client.Onboarding.Single(i => i.Name == "Photos").Received);
        // Ticking again keeps the first day.
        Assert.Equal(Today, Onboarding.Tick(client, "Photos", true, Today.AddDays(3)).Onboarding.Single(i => i.Name == "Photos").Received);
        Assert.Null(Onboarding.Tick(client, "Photos", false, Today).Onboarding.Single(i => i.Name == "Photos").Received);
    }

    [Fact]
    public void Items_can_be_added_once_and_removed()
    {
        var client = Onboarding.Add(Onboarding.Start(Bosveld, Today, ["Logo"]), " Testimonials ");
        Assert.Equal(["Logo", "Testimonials"], client.Onboarding.Select(i => i.Name));
        Assert.Equal(2, Onboarding.Add(client, "logo").Onboarding.Count);
        Assert.Equal(["Testimonials"], Onboarding.Remove(client, "Logo").Onboarding.Select(i => i.Name));
    }

    [Fact]
    public void Done_when_everything_is_in()
    {
        var client = Onboarding.Start(Bosveld, Today, ["Logo", "Photos"]);
        client = Onboarding.Tick(Onboarding.Tick(client, "Logo", true, Today), "Photos", true, Today);
        Assert.False(Onboarding.InProgress(client));
        Assert.Equal("All in", Onboarding.Progress(client));
    }

    [Fact]
    public void Stalled_after_a_quiet_week()
    {
        var started = Onboarding.Start(Bosveld, Today.AddDays(-9), ["Logo", "Photos"]);
        Assert.Equal(9, Assert.Single(Onboarding.Stalled([started], Today)).Days);
        var moving = Onboarding.Tick(started, "Logo", true, Today.AddDays(-2));
        Assert.Empty(Onboarding.Stalled([moving], Today));
        Assert.Empty(Onboarding.Stalled([Bosveld], Today));
    }

    [Fact]
    public void The_reminder_lists_what_is_outstanding()
    {
        var client = Onboarding.Tick(Onboarding.Start(Bosveld, Today, ["Logo (SVG or large PNG)", "Photos", "Deposit paid"]), "Deposit paid", true, Today);
        var (subject, body) = Onboarding.Reminder(client, "Webignite");
        Assert.Equal("What we still need for your website, Bosveld Builders", subject);
        Assert.StartsWith("Hi Pieter,", body);
        Assert.Contains("Thanks for what you've sent so far.", body);
        Assert.Contains("- Logo\n- Photos\n", body);
        Assert.DoesNotContain("Deposit", body);
    }

    [Fact]
    public void The_morning_summary_mentions_stalled_clients()
    {
        var client = Onboarding.Start(Bosveld, Today.AddDays(-8), ["Logo (SVG or large PNG)", "Photos", "Page content and text", "Sitemap signed off"]);
        var line = Assert.Single(MorningSummary.Build(new SummaryInput { Today = Today, Clients = [client] }));
        Assert.Equal("Nothing in from Bosveld Builders for 8 days. Still waiting on logo, photos, page content and text, and 1 more.", line.Text);
        Assert.Equal(SummaryTarget.Clients, line.Target);
        Assert.Same(client, line.Client);
    }
}
