using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;
using Webignite.Core.Tasks;

namespace Webignite.Desktop.ViewModels;

// My to-dos in My tasks, as Asana's: to-dos are my own tasks in no project, listed with the tasks given to me by
// when they're due. They stay in their own list, so automatic reminders, the client page and Claude or ChatGPT
// keep working as before; one moved into a project becomes a task there.
public sealed partial class ProjectsViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTodoCommand))]
    public partial string NewTodo { get; set; } = "";

    private bool CanAddTodo() => NewTodo.Trim().Length > 0;

    /// <summary>A to-do of my own, in no project, from the box at the top of My tasks.</summary>
    [RelayCommand(CanExecute = nameof(CanAddTodo))]
    private void AddTodo()
    {
        services.Store.SaveTask(new TaskItem { Id = TaskItem.NewId(), Title = NewTodo.Trim(), Created = DateTimeOffset.UtcNow });
        NewTodo = "";
        ShowMyTasks();
    }

    private IReadOnlyList<TaskItem> MyTodos()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        services.Store.RaiseAutomaticTasks(services.Settings.Tasks, today);
        return services.Store.ListTasks();
    }

    internal void TickTodo(TaskItem todo, bool done)
    {
        services.Store.ChangeTask(todo.Id, t => t with { Done = done, DoneAt = done ? DateTimeOffset.UtcNow : null });
        Feedback.After(done ? Feedback.DoneTime(services.Settings.Appearance.Motion) : TimeSpan.Zero, ShowMyTasks);
    }

    internal void SetTodoDue(TaskItem todo, DateOnly? due)
    {
        services.Store.ChangeTask(todo.Id, t => t with { Due = due });
        ShowMyTasks();
    }

    internal void DeleteTodo(TaskItem todo)
    {
        services.Store.DeleteTask(todo.Id);
        ShowMyTasks();
        services.OfferUndo($"Removed \"{todo.Title}\"", () =>
        {
            services.Store.SaveTask(todo);
            ShowMyTasks();
        });
    }

    /// <summary>The client, or the quote or invoice, a to-do is about.</summary>
    internal void OpenTodoAbout(TaskItem todo)
    {
        if (todo.DocumentId is { } id && services.Documents.Get(id) is { } stored)
            services.Go.Document(stored);
        else if (services.Clients.Find(todo.Customer) is { } client)
            services.Go.Client(client);
    }

    internal bool TodoHasAbout(TaskItem todo) => todo.DocumentId is not null || services.Clients.Find(todo.Customer) is not null;

    /// <summary>Running projects a to-do can move into, for its right-click menu.</summary>
    internal IReadOnlyList<Project> ProjectsToMoveInto() => [.. ProjectRules.Listed(services.Projects.Projects(), false)];

    /// <summary>Moves a to-do into a project: a task there, given to me, and opened; the to-do goes.</summary>
    internal void MoveTodoInto(TaskItem todo, Project project)
    {
        todo = services.Store.ListTasks().FirstOrDefault(t => t.Id == todo.Id) ?? todo;
        var task = MyWork.ToTask(todo, project.Id, services.Me().Id, services.Clients.Find(todo.Customer)?.Id, ProjectRules.NewId());
        var (saved, problems) = Ours(() => services.Projects.AddTask(task));
        if (saved is null)
        {
            MyMessage = string.Join(" ", problems);
            return;
        }
        services.Store.DeleteTask(todo.Id);
        ShowMyTasks();
        MyMessage = $"Moved \"{todo.Title}\" to {project.Name}.";
    }

    /// <summary>A word about the last thing done to a to-do in My tasks, shown under its heading.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMyMessage))]
    public partial string MyMessage { get; private set; } = "";

    public bool HasMyMessage => MyMessage.Length > 0;
}

/// <summary>A to-do in My tasks: tick it off, change when it's due, open what it's about, or move it into a project.</summary>
public sealed partial class TodoRow : ObservableObject
{
    private readonly ProjectsViewModel page;
    private readonly bool loading;

    public TodoRow(TaskItem todo, ProjectsViewModel page)
    {
        this.page = page;
        Todo = todo;
        loading = true;
        Done = todo.Done;
        loading = false;
    }

    public TaskItem Todo { get; }
    public string Title => Todo.Title;

    /// <summary>The line under its name: "To-do", with the client it's about.</summary>
    public string About => string.IsNullOrWhiteSpace(Todo.Customer) ? "To-do" : $"To-do · {Todo.Customer}";
    public bool IsAutomatic => Todo.Source == TaskSource.Auto;

    public string DueText => Todo.Due is { } due ? ProjectRules.DueLabel(due, DateOnly.FromDateTime(DateTime.Today)) : "Due date";
    public bool HasDue => Todo.Due is not null;
    public bool IsLate => Todo.Due is { } due && !Done && due < DateOnly.FromDateTime(DateTime.Today);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fade))]
    public partial bool Done { get; set; }

    public double Fade => Done ? 0.55 : 1;

    partial void OnDoneChanged(bool value)
    {
        if (!loading)
            page.TickTodo(Todo, value);
    }

    public void SetDue(DateOnly? due) => page.SetTodoDue(Todo, due);

    public bool HasAbout => page.TodoHasAbout(Todo);

    [RelayCommand]
    private void OpenAbout() => page.OpenTodoAbout(Todo);

    [RelayCommand]
    private void Delete() => page.DeleteTodo(Todo);

    public IReadOnlyList<Project> Projects => page.ProjectsToMoveInto();

    public void MoveInto(Project project) => page.MoveTodoInto(Todo, project);

    public string Tip => Todo.Notes is { Length: > 0 } notes ? notes : Title;

    public override string ToString() => Title.ToString(CultureInfo.CurrentCulture);
}
