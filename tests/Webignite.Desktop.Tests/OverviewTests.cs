using Webignite.Core.Layout;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>Overview is the person's to arrange: its own parts can be taken off, and widgets of their own added.</summary>
public sealed class OverviewTests
{
    [Fact]
    public Task A_new_install_shows_every_part_and_builds_no_widgets() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var overview = new OverviewViewModel(app.Services);

        Assert.True(overview.ShowFigures);
        Assert.True(overview.ShowToday);
        Assert.Null(overview.Board);
        Assert.False(overview.HoldsWork);
        overview.Dispose();
    });

    [Fact]
    public Task Each_part_can_be_taken_off_and_put_back_and_the_choice_is_kept() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var overview = new OverviewViewModel(app.Services);

        overview.ToggleFiguresCommand.Execute(null);
        overview.ToggleTodayCommand.Execute(null);
        overview.ToggleNeedsYouCommand.Execute(null);

        Assert.False(overview.ShowFigures);
        Assert.False(overview.ShowToday);
        Assert.False(overview.ShowNeedsYou);
        Assert.Equal(new OverviewParts { Figures = false, NeedsYou = false, Today = false }, app.Services.Settings.OverviewParts);
        overview.Dispose();

        // Opened again, it is as it was left.
        var again = new OverviewViewModel(app.Services);
        Assert.False(again.ShowFigures);
        again.ToggleFiguresCommand.Execute(null);
        Assert.True(again.ShowFigures);
        again.Dispose();
    });

    [Fact]
    public Task Widgets_added_to_Overview_are_kept_and_are_not_one_of_My_pages() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var overview = new OverviewViewModel(app.Services);

        overview.AddWidgetsCommand.Execute(null);
        overview.Board!.AddWidget(WidgetKind.Timer);
        overview.Board.AddWidget(WidgetKind.Note);

        Assert.True(overview.Board.IsHome);
        Assert.Equal(2, app.Services.Settings.OverviewBoard!.Widgets.Count);
        Assert.Empty(app.Services.Settings.MyPages);
        overview.Dispose();

        // Opened again, the widgets are there without being asked for.
        var again = new OverviewViewModel(app.Services);
        Assert.Equal([WidgetKind.Timer, WidgetKind.Note], again.Board!.Widgets.Select(w => w.Kind));

        // And My pages still has none of it.
        var pages = new MyPagesViewModel(app.Services);
        Assert.True(pages.NoPages);
        pages.Dispose();
        again.Dispose();
    });
}
