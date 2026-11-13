using Webignite.Core.Projects;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>Ticking a repeating task makes its next one, once, says so, and can be undone.</summary>
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
    public Task Ticking_it_in_the_list_makes_one_next_task_and_says_so() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, notes) = Open(app);

        page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id).Done = true;

        var copies = Copies(app, project);
        Assert.Equal(2, copies.Count);
        var next = Assert.Single(copies, t => !t.Done);
        Assert.Equal(new DateOnly(2026, 12, 13), next.Due);
        Assert.Equal(task.Id, next.RepeatOf);
        Assert.StartsWith("Done. It repeats, so the next one was made, due 13", Assert.Single(notes).What, StringComparison.Ordinal);
        page.Dispose();
    });

    [Fact]
    public Task Undo_takes_the_next_one_away_and_unticks_the_task() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, notes) = Open(app);
        page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id).Done = true;

        Assert.Single(notes).Undo!();

        var only = Assert.Single(Copies(app, project));
        Assert.Equal(task.Id, only.Id);
        Assert.False(only.Done);
        Assert.False(page.Rows.OfType<TaskRow>().Single().Done);
        page.Dispose();
    });

    [Fact]
    public Task Ticking_it_in_its_own_pane_makes_one_next_task_too() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, notes) = Open(app);
        page.OpenTaskById(task.Id);

        page.Detail!.Done = true;

        Assert.Equal(2, Copies(app, project).Count);
        Assert.Single(notes);
        page.Dispose();
    });

    [Fact]
    public Task Unticking_and_ticking_again_makes_no_second_next_task() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, _) = Open(app);

        TaskRow Row() => page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id);
        Row().Done = true;
        Row().Done = false;
        Row().Done = true;

        Assert.Equal(2, Copies(app, project).Count);
        page.Dispose();
    });

    [Fact]
    public Task A_task_that_doesnt_repeat_says_nothing_and_makes_nothing() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var (page, project, task, notes) = Open(app);
        app.Services.Projects.ChangeTask(task.Id, t => t with { Repeat = TaskRepeat.None });
        page.Refresh();

        page.Rows.OfType<TaskRow>().Single(r => r.Task.Id == task.Id).Done = true;

        Assert.Single(Copies(app, project));
        Assert.Empty(notes);
        page.Dispose();
    });
}
