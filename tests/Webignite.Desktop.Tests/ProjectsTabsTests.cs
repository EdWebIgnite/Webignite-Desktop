using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>Projects shows one view of a project at a time: the list, the board, files, the overview, the timeline, the calendar or messages.</summary>
public sealed class ProjectsTabsTests
{
    private static bool[] Showing(ProjectsViewModel page) =>
        [page.IsList, page.IsBoard, page.IsFiles, page.IsOverview, page.IsTimeline, page.IsCalendar, page.IsMessages];

    [Fact]
    public Task Exactly_one_view_shows_whichever_is_picked_and_in_whatever_order() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var page = new ProjectsViewModel(app.Services);
        Assert.Single(Showing(page), on => on);

        (System.Windows.Input.ICommand Pick, Func<bool> IsOn)[] views =
        [
            (page.BoardViewCommand, () => page.IsBoard),
            (page.FilesViewCommand, () => page.IsFiles),
            (page.OverviewViewCommand, () => page.IsOverview),
            (page.TimelineViewCommand, () => page.IsTimeline),
            (page.CalendarViewCommand, () => page.IsCalendar),
            (page.MessagesViewCommand, () => page.IsMessages),
            (page.ListViewCommand, () => page.IsList),
        ];
        // Each after each of the others, so no pair leaves two showing or none.
        foreach (var from in views)
        {
            foreach (var to in views)
            {
                from.Pick.Execute(null);
                to.Pick.Execute(null);
                Assert.True(to.IsOn());
                Assert.Single(Showing(page), on => on);
            }
        }
        page.Dispose();
    });

    [Fact]
    public Task The_tools_for_tasks_show_only_where_tasks_do() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var page = new ProjectsViewModel(app.Services);

        page.OverviewViewCommand.Execute(null);
        Assert.False(page.ShowsTaskTools);
        page.MessagesViewCommand.Execute(null);
        Assert.False(page.ShowsTaskTools);
        page.BoardViewCommand.Execute(null);
        Assert.True(page.ShowsTaskTools);
        Assert.True(page.ShowsSortGroup);
        page.TimelineViewCommand.Execute(null);
        Assert.True(page.ShowsTaskTools);
        Assert.False(page.ShowsSortGroup);
        page.Dispose();
    });
}
