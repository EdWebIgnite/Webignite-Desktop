using Webignite.Core.Layout;
using Webignite.Core.Projects;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>A task on the My tasks widget can be changed right there: its name, its day and its time.</summary>
public sealed class TaskWidgetTests
{
    private static (MyPagesViewModel Pages, TasksBody Body, ProjectTask Task) Open(TestApp app)
    {
        var project = app.Services.Projects.Add(new Project { Id = ProjectRules.NewId(), Name = "Bosveld Builders new website" }).Saved!;
        var task = app.Services.Projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "Wireframes", AssignedTo = app.Services.Me().Id }).Saved!;
        var pages = new MyPagesViewModel(app.Services);
        pages.NewPage();
        pages.AddWidget(WidgetKind.MyTasks);
        return (pages, (TasksBody)pages.Widgets.Single().Body, task);
    }

    private static TaskLine Line(TasksBody body) => body.Lines.OfType<TaskLine>().Single();

    [Fact]
    public Task Typing_over_a_tasks_name_renames_it() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (pages, body, task) = Open(app);

        Line(body).EditTitle = "  Wireframes for the home page ";

        Assert.Equal("Wireframes for the home page", app.Services.Projects.GetTask(task.Id)!.Title);
        // The widget shows it under its new name.
        Assert.Equal("Wireframes for the home page", Line(body).EditTitle);
        pages.Dispose();
    });

    [Fact]
    public Task An_emptied_name_puts_the_old_one_back() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (pages, body, task) = Open(app);
        var line = Line(body);

        line.EditTitle = "   ";

        Assert.Equal("Wireframes", line.EditTitle);
        Assert.Equal("Wireframes", app.Services.Projects.GetTask(task.Id)!.Title);
        pages.Dispose();
    });

    [Fact]
    public Task A_day_and_then_a_time_can_be_given_to_a_task() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (pages, body, task) = Open(app);
        Assert.False(Line(body).HasDue);

        Line(body).Due = new DateTime(2026, 12, 3);
        Assert.Equal(new DateOnly(2026, 12, 3), app.Services.Projects.GetTask(task.Id)!.Due);

        var line = Line(body);
        Assert.True(line.HasDue);
        line.TimeText = "14:30";
        Assert.Equal(new TimeOnly(14, 30), app.Services.Projects.GetTask(task.Id)!.DueTime);
        Assert.False(line.TimeProblem);
        pages.Dispose();
    });

    [Fact]
    public Task A_time_that_isnt_one_is_marked_and_changes_nothing() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (pages, body, task) = Open(app);
        Line(body).Due = new DateTime(2026, 12, 3);
        Line(body).TimeText = "9";

        var line = Line(body);
        line.TimeText = "later";

        Assert.True(line.TimeProblem);
        Assert.Equal(new TimeOnly(9, 0), app.Services.Projects.GetTask(task.Id)!.DueTime);
        pages.Dispose();
    });

    [Fact]
    public Task Clearing_the_day_clears_the_time_with_it() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (pages, body, task) = Open(app);
        Line(body).Due = new DateTime(2026, 12, 3);
        Line(body).TimeText = "14:30";

        Line(body).Due = null;

        var saved = app.Services.Projects.GetTask(task.Id)!;
        Assert.Null(saved.Due);
        Assert.Null(saved.DueTime);
        Assert.Equal("", Line(body).TimeText);
        pages.Dispose();
    });
}
