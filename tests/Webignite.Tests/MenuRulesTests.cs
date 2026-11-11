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
}
