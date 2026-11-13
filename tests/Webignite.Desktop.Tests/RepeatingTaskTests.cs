using Webignite.Core.Projects;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>Ticking a repeating task keeps the one task: it is noted as done, comes back unticked for its next day, and can be undone.</summary>
public sealed class RepeatingTaskTests
{
    private static (ProjectsViewModel Page, Project Project, ProjectTask Task, List<(string What, Action? Undo)> Notes) Open(TestApp app)
    {
        var notes = new List<(string, Action?)>();
        app.Services.ShowNote = (what, _, undo) => notes.Add((what, undo));
        var project = app.Services.Projects.Add(new Project { Id = ProjectRules.NewId(), Name = "Karoo Coffee care plan" }).Saved!;
        var task = app.Services.Projects.AddTask(new ProjectTask
        {
            Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "Monthly maintenance", Repeat = TaskRepeat.Monthly, Due = new DateOnly(2026, 11, 13),
        }).Saved!;
        var page = new ProjectsViewModel(app.Services);
        page.OpenProject(project.Id);
        return (page, project, task, notes);
    }

    private static List<ProjectTask> Copies(TestApp app, Project project) =>
        [.. app.Services.Projects.AllTasks(project.Id).Where(t => t.Title == "Monthly maintenance")];

    [Fact]
    public Task Ticking_it_in_the_list_keeps_the_one_task_unticked_for_next_month_and_says_so() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, notes) = Open(app);

        page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id).Done = true;

        var only = Assert.Single(Copies(app, project));
        Assert.Equal(task.Id, only.Id);
        Assert.False(only.Done);
        Assert.Equal(new DateOnly(2026, 12, 13), only.Due);
        // The list shows it as it is now: not ticked, with its new date.
        var row = page.Rows.OfType<TaskRow>().Single();
        Assert.False(row.Done);
        Assert.Equal(new DateTime(2026, 12, 13), row.Due);
        Assert.StartsWith("Done for 13", Assert.Single(notes).What, StringComparison.Ordinal);
        Assert.Contains("It repeats, so it is due again on 13", notes[0].What, StringComparison.Ordinal);
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
        Assert.Equal(new DateOnly(2026, 11, 13), only.Due);
        Assert.False(only.Done);
        Assert.Equal(new DateTime(2026, 11, 13), page.Rows.OfType<TaskRow>().Single().Due);
        page.Dispose();
    });

    [Fact]
    public Task Ticking_it_in_its_own_pane_does_the_same_and_the_pane_shows_it_unticked() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, notes) = Open(app);
        page.OpenTaskById(task.Id);

        page.Detail!.Done = true;

        var only = Assert.Single(Copies(app, project));
        Assert.False(only.Done);
        Assert.Equal(new DateOnly(2026, 12, 13), only.Due);
        Assert.False(page.Detail.Done);
        Assert.Equal(new DateTime(2026, 12, 13), page.Detail.Due);
        Assert.Single(notes);
        page.Dispose();
    });

    [Fact]
    public Task Each_tick_moves_it_on_one_more_month_and_never_makes_a_copy() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, _) = Open(app);

        TaskRow Row() => page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id);
        Row().Done = true;
        Row().Done = true;
        Row().Done = true;

        Assert.Equal(new DateOnly(2027, 2, 13), Assert.Single(Copies(app, project)).Due);
        Assert.Equal(3, app.Services.Projects.Activity(task.Id).Count(a => a.Kind == ActivityKind.DoneThisTime));
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

        Assert.True(Assert.Single(Copies(app, project)).Done);
        Assert.Empty(notes);
        page.Dispose();
    });
}
