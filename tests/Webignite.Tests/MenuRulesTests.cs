using Webignite.Core.Documents;
using Webignite.Core.Layout;

namespace Webignite.Tests;

public sealed class MenuRulesTests
{
    [Fact]
    public void The_dock_starts_with_the_usual_pages_and_keeps_only_ones_the_app_has()
    {
        string[] known = ["Overview", "Mail", "Clients", "Invoices", "Files", "Notes"];
        Assert.Equal(["Overview", "Mail", "Clients", "Invoices", "Files"], DockPages.Shown(null, known));
        Assert.Equal(["Notes", "Mail"], DockPages.Shown(["Notes", "Gone", "Mail", "Notes"], known));
        Assert.Empty(DockPages.Shown([], known));
        Assert.Equal(DockPages.Most, DockPages.Shown([.. Enumerable.Range(1, 30).Select(i => "P" + i)], [.. Enumerable.Range(1, 30).Select(i => "P" + i)]).Count);
    }

    [Fact]
    public void A_page_is_added_to_the_end_of_the_dock_once_while_there_is_room()
    {
        IReadOnlyList<string> dock = ["Overview", "Mail"];
        Assert.True(DockPages.CanAdd(dock, "Notes"));
        Assert.Equal(["Overview", "Mail", "Notes"], DockPages.Add(dock, "Notes"));
        Assert.False(DockPages.CanAdd(dock, "Mail"));
        Assert.Same(dock, DockPages.Add(dock, "Mail"));

        IReadOnlyList<string> full = [.. Enumerable.Range(1, DockPages.Most).Select(i => "P" + i)];
        Assert.False(DockPages.CanAdd(full, "Notes"));
        Assert.Same(full, DockPages.Add(full, "Notes"));
    }

    [Fact]
    public void A_page_comes_off_the_dock_but_never_the_last_one()
    {
        IReadOnlyList<string> dock = ["Overview", "Mail"];
        Assert.Equal(["Overview"], DockPages.Remove(dock, "Mail"));
        Assert.False(DockPages.CanRemove(dock, "Notes"));
        Assert.Same(dock, DockPages.Remove(dock, "Notes"));
        IReadOnlyList<string> one = ["Overview"];
        Assert.False(DockPages.CanRemove(one, "Overview"));
        Assert.Same(one, DockPages.Remove(one, "Overview"));
    }

    [Fact]
    public void With_nothing_owed_or_waiting_the_menu_says_nothing()
    {
        Assert.Empty(MenuFigures.For([], new DateOnly(2026, 11, 11)));
        Assert.Equal("3 unread", MenuFigures.For([], new DateOnly(2026, 11, 11), unreadMail: 3)["Mail"].Text);
    }

    [Fact]
    public void The_menu_says_what_is_owed_and_marks_it_late_when_any_of_it_is()
    {
        var today = Samples.Today;
        StoredDocument[] documents = [Samples.InXero("1", 2725m, today.AddDays(-14)), Samples.InXero("2", 4100.40m, today.AddDays(7))];
        var owed = Receivables.Owed(documents);
        Assert.Equal(6825.40m, owed);

        var figures = MenuFigures.For(documents, today);
        // Whole rands, spaced as every amount in the app is (Money.Rand).
        Assert.Equal(new MenuFigure(Money.Rand(6825m)[..^3] + " owed", Late: true), figures["Invoices"]);
        Assert.False(MenuFigures.For([documents[1]], today)["Invoices"].Late);
        Assert.Equal(Money.Rand(4100m)[..^3] + " owed", MenuFigures.For([documents[1]], today)["Invoices"].Text);
        Assert.False(figures.ContainsKey("Quotes"));
    }

    [Fact]
    public void The_menu_counts_quotes_out_and_what_waits_for_approval_as_overview_does()
    {
        var today = Samples.Today;
        StoredDocument[] documents = [Samples.InXero("7", 17700m, today.AddDays(20), DocumentKind.Quote), Samples.Stored()];
        var figures = MenuFigures.For(documents, today, unreadMail: 2);

        var quotesOut = Receivables.QuotesOut(documents, today);
        Assert.Equal(quotesOut > 0, figures.ContainsKey("Quotes"));
        if (quotesOut > 0)
            Assert.Equal(Money.Rand(17700m)[..^3] + " out", figures["Quotes"].Text);
        var waiting = Approvals.Waiting(documents).Count;
        Assert.Equal(waiting > 0, figures.ContainsKey("Approvals"));
        if (waiting > 0)
            Assert.Equal($"{waiting} waiting", figures["Approvals"].Text);
        Assert.Equal("2 unread", figures["Mail"].Text);
    }

    private static Webignite.Core.Renewals.Renewal Domain(string id, DateOnly renewsOn) => new()
    {
        Id = id, Name = id + ".co.za", ClientName = "Karoo Coffee Roasters", Provider = "domains.co.za", RenewsOn = renewsOn, Cost = 99m,
    };

    private static Webignite.Core.Retainers.Retainer Monthly(string id, int day, bool paused = false) => new()
    {
        Id = id, ClientName = "Fynbos Physio", Title = "Monthly care", Lines = [Samples.Priced("Care, [Month]", 1, 900m)], DayOfMonth = day,
        Starts = new DateOnly(2026, 1, 1), Paused = paused,
    };

    [Fact]
    public void Domains_say_what_is_past_its_date_first_then_what_renews_next()
    {
        var today = Samples.Today; // Saturday 31 Oct 2026
        var far = Domain("far", today.AddDays(200));
        var soon = Domain("soon", today.AddDays(2));
        var sooner = Domain("sooner", today.AddDays(1));
        var late = Domain("late", today.AddDays(-3));

        Assert.False(MenuFigures.For(new MenuFacts { Today = today, Renewals = [far] }).ContainsKey("Renewals"));
        Assert.Equal(new MenuFigure("1 renews Monday"), MenuFigures.For(new MenuFacts { Today = today, Renewals = [far, soon] })["Renewals"]);
        Assert.Equal(new MenuFigure("1 renews tomorrow"), MenuFigures.For(new MenuFacts { Today = today, Renewals = [sooner] })["Renewals"]);
        Assert.Equal(new MenuFigure("2 renew soon"), MenuFigures.For(new MenuFacts { Today = today, Renewals = [soon, sooner] })["Renewals"]);
        Assert.Equal(new MenuFigure("1 overdue", Late: true), MenuFigures.For(new MenuFacts { Today = today, Renewals = [soon, late, far] })["Renewals"]);
    }

    [Fact]
    public void Retainers_say_how_many_go_out_next_and_when()
    {
        var today = new DateOnly(2026, 11, 11);
        var facts = new MenuFacts { Today = today, Retainers = [Monthly("a", 25), Monthly("b", 25), Monthly("c", 28), Monthly("d", 25, paused: true)] };
        Assert.Equal(new MenuFigure("2 go out on the 25th"), MenuFigures.For(facts)["Retainers"]);
        Assert.Equal(new MenuFigure("1 goes out on the 28th"), MenuFigures.For(facts with { Retainers = [Monthly("c", 28)] })["Retainers"]);
        Assert.Equal(new MenuFigure("1 goes out today"), MenuFigures.For(facts with { Retainers = [Monthly("e", 11)] })["Retainers"]);
        // A retainer set for the 31st goes out on the last day of a shorter month, and the menu says that day.
        Assert.Equal(new MenuFigure("1 goes out on the 30th"), MenuFigures.For(facts with { Retainers = [Monthly("f", 31)] })["Retainers"]);
        Assert.Equal(new MenuFigure("1 goes out on the 31st"), MenuFigures.For(facts with { Today = new DateOnly(2026, 12, 3), Retainers = [Monthly("f", 31)] })["Retainers"]);
        Assert.False(MenuFigures.For(facts with { Retainers = [Monthly("d", 25, paused: true)] }).ContainsKey("Retainers"));

        // This month's already drafted: the next one is next month's, said by its date.
        var drafted = new HashSet<string> { Webignite.Core.Retainers.RetainerSchedule.Key(Monthly("a", 25), new DateOnly(2026, 11, 1)) };
        Assert.Equal(new MenuFigure("1 goes out 25 Dec"), MenuFigures.For(facts with { Retainers = [Monthly("a", 25)], Drafted = drafted })["Retainers"]);
    }

    [Fact]
    public void Money_and_clients_say_what_came_in_and_what_is_being_built()
    {
        var today = Samples.Today;
        Assert.False(MenuFigures.For(new MenuFacts { Today = today }).ContainsKey("Money"));
        Assert.Equal(Money.Rand(10050m)[..^3] + " in", MenuFigures.For(new MenuFacts { Today = today, ReceivedThisMonth = 10049.60m })["Money"].Text);

        var building = Webignite.Core.Clients.Onboarding.Start(new Webignite.Core.Clients.Client { Id = "a", Name = "Bosveld Builders" }, today);
        var settled = new Webignite.Core.Clients.Client { Id = "b", Name = "Fynbos Physio" };
        Assert.True(Webignite.Core.Clients.Onboarding.InProgress(building));
        Assert.Equal("1 new site", MenuFigures.For(new MenuFacts { Today = today, Clients = [building, settled] })["Clients"].Text);
        Assert.Equal("2 new sites", MenuFigures.For(new MenuFacts { Today = today, Clients = [building, building with { Id = "c" }] })["Clients"].Text);
        Assert.False(MenuFigures.For(new MenuFacts { Today = today, Clients = [settled] }).ContainsKey("Clients"));
    }
}
