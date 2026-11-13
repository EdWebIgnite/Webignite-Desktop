using Webignite.Core.Projects;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>Ticking a repeating task keeps the one task: it is noted as done, shows as done until shortly before its next day, and can be undone.</summary>
public sealed class RepeatingTaskTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private static readonly DateOnly Next = Today.AddMonths(1);

    private static (ProjectsViewModel Page, Project Project, ProjectTask Task, List<(string What, Action? Undo)> Notes) Open(TestApp app)
    {
        var notes = new List<(string, Action?)>();
        app.Services.ShowNote = (what, _, undo) => notes.Add((what, undo));
        var project = app.Services.Projects.Add(new Project { Id = ProjectRules.NewId(), Name = "Karoo Coffee care plan" }).Saved!;
        var task = app.Services.Projects.AddTask(new ProjectTask
        {
            Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "Monthly maintenance", Repeat = TaskRepeat.Monthly, Due = Today,
        }).Saved!;
        var page = new ProjectsViewModel(app.Services);
        page.OpenProject(project.Id);
        return (page, project, task, notes);
    }

    private static List<ProjectTask> Copies(TestApp app, Project project) =>
        [.. app.Services.Projects.AllTasks(project.Id).Where(t => t.Title == "Monthly maintenance")];

    [Fact]
    public Task Ticking_it_in_the_list_keeps_the_one_task_done_until_a_week_before_next_month_and_says_so() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, notes) = Open(app);

        page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id).Done = true;

        var only = Assert.Single(Copies(app, project));
        Assert.Equal(task.Id, only.Id);
        Assert.True(only.Done);
        Assert.Equal(Next, only.Due);
        Assert.Equal(Next.AddDays(-7), only.BackOn);
        // The list shows it done, with its next date beside it.
        var row = page.Rows.OfType<TaskRow>().Single();
        Assert.True(row.Done);
        Assert.Equal(Next.ToDateTime(TimeOnly.MinValue), row.Due);
        Assert.StartsWith("Done for ", Assert.Single(notes).What, StringComparison.Ordinal);
        Assert.Contains(" Back on ", notes[0].What, StringComparison.Ordinal);
        page.Dispose();
    });

    [Fact]
    public Task It_is_off_my_tasks_until_it_comes_back() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, _, task, _) = Open(app);
        var me = app.Services.Team().Me!.Id;
        app.Services.Projects.ChangeTask(task.Id, t => t with { AssignedTo = me });
        Assert.Contains(app.Services.Projects.Mine(me), t => t.Id == task.Id);
        page.Refresh();

        page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id).Done = true;

        Assert.DoesNotContain(app.Services.Projects.Mine(me), t => t.Id == task.Id);
        page.Dispose();
    });

    [Fact]
    public Task Its_history_says_it_was_ticked_off() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, _, task, _) = Open(app);

        page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id).Done = true;

        Assert.Single(app.Services.Projects.Activity(task.Id), a => a.Kind == ActivityKind.DoneThisTime);
        page.Dispose();
    });

    [Fact]
    public Task Undo_puts_the_task_back_on_the_day_it_was_due() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, notes) = Open(app);
        page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id).Done = true;

        Assert.Single(notes).Undo!();

        var only = Assert.Single(Copies(app, project));
        Assert.Equal(Today, only.Due);
        Assert.False(only.Done);
        Assert.Null(only.BackOn);
        var row = page.Rows.OfType<TaskRow>().Single();
        Assert.False(row.Done);
        Assert.Equal(Today.ToDateTime(TimeOnly.MinValue), row.Due);
        page.Dispose();
    });

    [Fact]
    public Task Ticking_it_in_its_own_pane_does_the_same() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, notes) = Open(app);
        page.OpenTaskById(task.Id);

        page.Detail!.Done = true;

        var only = Assert.Single(Copies(app, project));
        Assert.True(only.Done);
        Assert.Equal(Next, only.Due);
        Assert.True(page.Detail.Done);
        Assert.Equal(Next.ToDateTime(TimeOnly.MinValue), page.Detail.Due);
        Assert.Single(notes);
        page.Dispose();
    });

    [Fact]
    public Task Ticking_again_while_it_waits_moves_nothing_and_makes_no_copy() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, notes) = Open(app);

        page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id).Done = true;
        app.Services.Projects.ChangeTask(task.Id, t => t with { Done = true });
        page.Refresh();

        Assert.Equal(Next, Assert.Single(Copies(app, project)).Due);
        Assert.Single(notes);
        page.Dispose();
    });

    [Fact]
    public Task A_task_that_doesnt_repeat_is_simply_done_and_nothing_is_said() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, notes) = Open(app);
        app.Services.Projects.ChangeTask(task.Id, t => t with { Repeat = TaskRepeat.None });
        page.Refresh();

        page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id).Done = true;

        var only = Assert.Single(Copies(app, project));
        Assert.True(only.Done);
        Assert.Null(only.BackOn);
        Assert.Empty(notes);
        page.Dispose();
    });
}
