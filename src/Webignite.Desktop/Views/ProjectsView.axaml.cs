using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Webignite.Core.Projects;
using Webignite.Desktop.Controls;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// The Projects page. A task is dragged by any part of its row or card, as in Asana: the line shows where it will
/// land, above or below another task, at the top or end of a heading, or into a board column. Dropped on another
/// project in the sidebar, it moves there.
/// </summary>
public partial class ProjectsView : FullBleedView
{
    public ProjectsView()
    {
        InitializeComponent();
        var tasks = new DragRules
        {
            CanDrag = item => item is TaskRow,
            CanDrop = (item, target, below) => target switch
            {
                TaskRow row => row != item,
                SectionRow => below,
                AddTaskRow => !below,
                BoardColumn => true,
                _ => false,
            },
            Drop = (item, target, below) => Projects?.Drop((TaskRow)item, target, below),
            IsInto = item => item is BoardColumn,
            Label = item => (item as TaskRow)?.Title ?? "",
        };
        DragReorder.Attach(TaskList, tasks);
        DragReorder.Attach(Board, tasks);
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

    // Who a task is for: the names to pick from, shown under the name clicked. Built only when clicked, so a long
    // list doesn't carry a drop-down for every row.
    private void WhoClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not TaskRow row)
            return;
        var list = new ListBox { ItemsSource = row.People, SelectedItem = row.AssignedTo, MinWidth = 180 };
        var flyout = new Flyout { Content = list, Placement = PlacementMode.BottomEdgeAlignedLeft };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is Choice<string?> picked && picked != row.AssignedTo)
                row.AssignedTo = picked;
            flyout.Hide();
        };
        flyout.ShowAt(button);
    }

    // When a task is due: a month to pick a day from, and a way to take the date off.
    private void DueClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not TaskRow row)
            return;
        var calendar = new Avalonia.Controls.Calendar { SelectedDate = row.Due, DisplayDate = row.Due ?? DateTime.Today };
        var clear = new Button { Content = "No due date", IsVisible = row.Due is not null, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        clear.Classes.Add("link");
        var flyout = new Flyout { Content = new StackPanel { Spacing = 6, Children = { calendar, clear } }, Placement = PlacementMode.BottomEdgeAlignedRight };
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDate is { } day && day.Date != row.Due?.Date)
                row.Due = day.Date;
            flyout.Hide();
        };
        clear.Click += (_, _) =>
        {
            row.Due = null;
            flyout.Hide();
        };
        flyout.ShowAt(button);
    }
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

    /// <summary>The templates to start a project from, then removing one.</summary>
    private void TemplatesOpening(object? sender, EventArgs e)
    {
        if (Projects is not { } projects || sender is not MenuFlyout flyout)
            return;
        flyout.Items.Clear();
        foreach (var template in projects.Templates)
        {
            var item = new MenuItem { Header = $"New from {template.Name}" };
            ToolTip.SetTip(item, $"{template.Headings.Count} headings, {template.Tasks.Count} tasks. Uses the name typed, if any; dates count from today.");
            item.Click += (_, _) => projects.NewFromTemplate(template);
            flyout.Items.Add(item);
        }
        if (projects.Templates.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = "No templates yet. Open a project and click Save as template.", IsEnabled = false });
            return;
        }
        flyout.Items.Add(new Separator());
        var remove = new MenuItem { Header = "Remove a template" };
        foreach (var template in projects.Templates)
        {
            var item = new MenuItem { Header = template.Name };
            item.Click += (_, _) => projects.RemoveTemplate(template);
            remove.Items.Add(item);
        }
        flyout.Items.Add(remove);
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
}
