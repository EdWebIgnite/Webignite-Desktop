using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Webignite.Core.Projects;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The Projects page. Tasks are dragged by their grip: the line shows where the task will land, above or below
/// another task, or at the top of a heading.
/// </summary>
public partial class ProjectsView : FullBleedView
{
    private TaskRow? dragging;
    private Control dragHost = null!;
    private object? dropOn;
    private bool dropBelow;

    public ProjectsView()
    {
        InitializeComponent();
        TaskList.AddHandler(PointerMovedEvent, Dragged, RoutingStrategies.Tunnel, handledEventsToo: true);
        TaskList.AddHandler(PointerReleasedEvent, Dropped, RoutingStrategies.Tunnel, handledEventsToo: true);
        TaskList.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble);
        Board.AddHandler(PointerMovedEvent, Dragged, RoutingStrategies.Tunnel, handledEventsToo: true);
        Board.AddHandler(PointerReleasedEvent, Dropped, RoutingStrategies.Tunnel, handledEventsToo: true);
        Board.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble);
        NewProjectBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Projects is { } projects)
            {
                projects.NewProjectCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private ProjectsViewModel? Projects => DataContext as ProjectsViewModel;
    private ProjectsViewModel? watching;
    private bool tuckedList;

    // In a narrow window, opening a task tucks the project list away (its strip brings it back), and closing the
    // task puts it back, so the task's pane has room beside the project.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (watching is not null)
            watching.PropertyChanged -= Watch;
        watching = Projects;
        if (watching is not null)
            watching.PropertyChanged += Watch;
    }

    private void Watch(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProjectsViewModel.HasTask) || Projects is not { } projects)
            return;
        var list = projects.Panels["list"];
        if (projects.HasTask && list.IsShown && Bounds.Width > 0 && Bounds.Width < 1400)
        {
            list.IsShown = false;
            tuckedList = true;
        }
        else if (!projects.HasTask && tuckedList)
        {
            list.IsShown = true;
            tuckedList = false;
        }
    }

    private void PaneResized(string pane, double width) => Projects?.Panels.Resized(pane, width);

    // One-way, so a new list of clients arriving never changes the project's client; only a real pick does.
    private void ClientPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (Projects is { } projects && ClientBox.SelectedItem is Choice<Webignite.Core.Clients.Client?> choice && choice != projects.EditClient)
            projects.PickClient(choice);
    }

    private void StatusPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (Projects is { } projects && StatusBox.SelectedItem is Choice<ProjectStatus> choice && choice != projects.EditStatus)
            projects.PickStatus(choice);
    }

    private void AddTaskKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as Control)?.DataContext is AddTaskRow add)
        {
            add.AddCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void AddCardKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as Control)?.DataContext is BoardColumn column)
        {
            column.Add.AddCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void AddHeadingKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Projects is { } projects)
        {
            projects.AddHeadingCommand.Execute(null);
            e.Handled = true;
        }
    }

    // The task pane's lists are one-way, so a list rebuilt never changes the task; only a real pick does.
    private void HeadingPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (Projects?.Detail is { } task && HeadingBox.SelectedItem is Choice<string?> choice && choice != task.Heading)
            task.PickHeading(choice);
    }

    private void TaskClientPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (Projects?.Detail is { } task && TaskClientBox.SelectedItem is Choice<Webignite.Core.Clients.Client?> choice && choice != task.Client)
            task.PickClient(choice);
    }

    private void DocumentPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (Projects?.Detail is { } task && DocumentBox.SelectedItem is Choice<Webignite.Core.Documents.StoredDocument?> choice && choice != task.Document)
            task.PickDocument(choice);
    }

    private void PriorityFilterPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (Projects is { } projects && PriorityFilterBox.SelectedItem is Choice<TaskPriority?> choice && choice != projects.FilterPriority)
            projects.PickPriorityFilter(choice);
    }

    private void TagFilterPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (Projects is { } projects && TagFilterBox.SelectedItem is Choice<string?> choice && choice != projects.FilterTag)
            projects.PickTagFilter(choice);
    }

    private void AddSubtaskKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Projects?.Detail is { } task)
        {
            task.AddSubtaskCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Right-click on a task: move it to another heading, or delete it.
    private void TaskMenu(object? sender, ContextRequestedEventArgs e)
    {
        if (Projects is not { } projects || (sender as Control)?.DataContext is not TaskRow row)
            return;
        var move = new MenuItem { Header = "Move to" };
        foreach (var section in projects.Sections.Where(s => s != row.Section))
        {
            var item = new MenuItem { Header = section.IsHeading ? section.Name : "Not under a heading" };
            item.Click += (_, _) => projects.MoveTo(row, section);
            move.Items.Add(item);
        }
        move.IsEnabled = move.Items.Count > 0;
        var delete = new MenuItem { Header = "Delete the task" };
        delete.Click += (_, _) => row.DeleteCommand.Execute(null);
        new ContextMenu { ItemsSource = new object[] { move, new Separator(), delete } }.Open(sender as Control);
        e.Handled = true;
    }

    // Dragging a task by its grip

    private void GripPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TaskRow row || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        dragging = row;
        dragHost = Projects?.IsBoard == true ? Board : TaskList;
        e.Pointer.Capture(dragHost);
        e.Handled = true;
    }

    private void Dragged(object? sender, PointerEventArgs e)
    {
        if (dragging is null)
            return;
        Mark(null, false);
        var point = e.GetPosition(dragHost);
        if (dragHost.InputHitTest(point) is not Visual hit)
            return;
        // The nearest row or card under the pointer; on the board, a column's empty space means its end.
        foreach (var visual in hit.GetSelfAndVisualAncestors())
        {
            if (visual == dragHost)
                break;
            if (visual is ContentPresenter { DataContext: TaskRow or SectionRow or AddTaskRow or BoardColumn } container && container.GetVisualParent() is Panel)
            {
                var top = container.TranslatePoint(default, dragHost)?.Y ?? 0;
                Mark(container.DataContext, point.Y > top + container.Bounds.Height / 2);
                return;
            }
        }
    }

    private void Dropped(object? sender, PointerReleasedEventArgs e)
    {
        if (dragging is not { } row)
            return;
        var (target, below) = (dropOn, dropBelow);
        EndDrag();
        e.Pointer.Capture(null);
        if (target is not null && target != row && Projects is { } projects)
            projects.Drop(row, target, below);
    }

    private void EndDrag()
    {
        dragging = null;
        Mark(null, false);
    }

    private void Mark(object? target, bool below)
    {
        switch (dropOn)
        {
            case TaskRow t:
                t.DropAbove = t.DropBelow = false;
                break;
            case SectionRow s:
                s.DropHere = false;
                break;
            case BoardColumn c:
                c.DropHere = false;
                break;
        }
        dropOn = target;
        dropBelow = below;
        switch (target)
        {
            case TaskRow t when t != dragging:
                t.DropAbove = !below;
                t.DropBelow = below;
                break;
            case SectionRow s:
                s.DropHere = true;
                break;
            case BoardColumn c:
                c.DropHere = true;
                break;
        }
    }
}
