using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
        // Tasks drag by any part of their row or card; headings by their name, in the list or as board columns.
        var tasks = new DragRules
        {
            // Sorted or grouped by something else, the list keeps its order: nothing is dragged into place, as in Asana.
            CanDrag = item => Projects?.CanReorder == true
                && item is TaskRow { IsSubtask: false } or SectionRow { IsHeading: true } or BoardColumn { Section.IsHeading: true },
            CanDrop = (item, target, below) => (item, target) switch
            {
                (TaskRow row, TaskRow on) => on != row && !on.IsSubtask && !(Projects?.Moving(row).Contains(on.Task) ?? false),
                (TaskRow, SectionRow) => below,
                (TaskRow, AddTaskRow) => !below,
                (TaskRow, BoardColumn) => true,
                (SectionRow heading, _) => Projects?.CanMoveHeading(heading, target, below) == true,
                (BoardColumn column, BoardColumn) => Projects?.CanMoveHeading(column.Section, target, below) == true,
                _ => false,
            },
            Drop = (item, target, below) =>
            {
                switch (item)
                {
                    case TaskRow row:
                        Projects?.Drop(row, target, below);
                        break;
                    case SectionRow heading:
                        Projects?.MoveHeading(heading, target, below);
                        break;
                    case BoardColumn column:
                        Projects?.MoveHeading(column.Section, target, below);
                        break;
                }
            },
            IsInto = (item, target) => item is TaskRow && target is BoardColumn,
            Across = target => target is BoardColumn,
            Label = item => item switch
            {
                TaskRow row when row.Moving.Count > 1 => $"{row.Moving.Count} tasks",
                TaskRow row => row.Title,
                SectionRow heading => heading.Name,
                BoardColumn column => column.Section.Name,
                _ => "",
            },
        };
        DragReorder.Attach(TaskList, tasks);
        DragReorder.Attach(Board, tasks);

        // The open task's subtasks drag into order; one dragged onto the list becomes a task there, and a task
        // dragged from the list onto the subtasks becomes one of them.
        DragReorder.Attach(SubtaskList, new DragRules
        {
            CanDrag = item => item is TaskRow,
            CanDrop = (item, target, _) => item is TaskRow && target is TaskRow on && on != item,
            Drop = (item, target, below) => Projects?.MoveSubtask((TaskRow)item, (TaskRow)target, below),
            Label = item => (item as TaskRow)?.Title ?? "",
        });
        foreach (var list in new Control[] { TaskList, Board })
            DropZones.Add(new DropZone(list, ProjectsViewModel.TakesSubtaskBack, (item, target) => Projects?.Unnest((TaskRow)item, target)));
        DropZones.Add(new DropZone(SubtaskArea, (item, _) => Projects?.TakesAsSubtask(item) == true, (item, _) => Projects?.MakeSubtasks((TaskRow)item))
        {
            Whole = true,
        });

        // Files dropped on the open task are attached to it.
        TaskPane.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            e.DragEffects = Projects?.HasTask == true && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        TaskPane.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (Projects is { Detail: { } task } projects && e.DataTransfer.TryGetFiles() is { } files)
                projects.Attach(task.Task.Id, files.Select(f => f.TryGetLocalPath()).OfType<string>());
        });

        // Narrow (a task open beside it), the list drops Priority and closes its cells up, as Asana's does.
        ListArea.SizeChanged += (_, e) => ListArea.Classes.Set("narrowlist", e.NewSize.Width < 820);

        // Ctrl+click picks a task to drag along with others, Shift+click a run of them; a plain click elsewhere lets them go.
        TaskList.AddHandler(PointerPressedEvent, PickPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Board.AddHandler(PointerPressedEvent, PickPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
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

    // Add task, as Asana's button: the cursor goes to the box under the first heading, ready to type.
    private void AddTaskClicked(object? sender, RoutedEventArgs e)
    {
        if (Projects is not { } projects)
            return;
        projects.ListViewCommand.Execute(null);
        if (projects.Rows.OfType<SectionRow>().FirstOrDefault(r => r.IsFolded) is { } folded && folded == projects.Rows.OfType<SectionRow>().FirstOrDefault())
            projects.Fold(folded);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (projects.Rows.OfType<AddTaskRow>().FirstOrDefault() is { } add && TaskList.ContainerFromItem(add) is { } container
                && container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } box)
            {
                box.BringIntoView();
                box.Focus();
            }
        });
    }

    private void AddHeadingClicked(object? sender, RoutedEventArgs e)
    {
        NewHeadingBox.BringIntoView();
        NewHeadingBox.Focus();
    }

    // How much a task matters: High, Normal or Low, from its cell.
    private void PriorityClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not TaskRow row)
            return;
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var priority in new[] { TaskPriority.High, TaskPriority.Normal, TaskPriority.Low })
        {
            var item = new MenuItem { Header = ProjectRules.Label(priority) };
            item.Click += (_, _) => row.SetPriority(priority);
            menu.Items.Add(item);
        }
        menu.ShowAt(button);
    }

    private void PickPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Projects is not { } projects || sender is not Control host || !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            return;
        var (_, item) = DragReorder.FindItem(host, e.Source as Visual, data => data is TaskRow);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (item is TaskRow row && (ctrl || shift))
        {
            projects.Pick(row, run: shift);
            e.Handled = true;
        }
        else if (item is not TaskRow { IsPicked: true })
            projects.ClearPicks();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Projects is { PickedCount: > 0 } projects)
        {
            projects.ClearPicks();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

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

    // Enter adds the subtask and leaves the box ready for the next, as in Asana; Esc stops adding.
    private void AddSubtaskKey(object? sender, KeyEventArgs e)
    {
        if (Projects?.Detail is not { } task)
            return;
        if (e.Key == Key.Enter)
        {
            task.AddSubtaskCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => NewSubtaskBox.Focus());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            task.AddingSubtask = false;
            e.Handled = true;
        }
    }

    // The + by Subtasks (and in the pane's toolbar): the box to type one, with the cursor in it.
    private void AddSubtaskClicked(object? sender, RoutedEventArgs e)
    {
        if (Projects?.Detail is not { } task)
            return;
        task.StartSubtaskCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            NewSubtaskBox.BringIntoView();
            NewSubtaskBox.Focus();
        });
    }

    // Attach files to the open task: picked here, or dropped on the task pane from Windows or the Files page.
    private void AttachClicked(object? sender, RoutedEventArgs e) => AttachAsync().Forget("Attaching files", tell: true);

    private async Task AttachAsync()
    {
        if (Projects?.Detail is not { } task)
            return;
        var paths = await Webignite.Desktop.Files.Picker.Files(this, "Attach files", many: true);
        if (paths.Count > 0)
            Projects?.Attach(task.Task.Id, paths);
    }

    // An attached file's right-click menu: open it, show it in its folder, the task it's on, the cover, remove it.
    private void AttachmentMenu(object? sender, ContextRequestedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not AttachmentRow row)
            return;
        var items = new List<object>();
        MenuItem Item(string header, System.Windows.Input.ICommand command)
        {
            var item = new MenuItem { Header = header, Command = command };
            items.Add(item);
            return item;
        }
        Item("Open", row.OpenCommand);
        Item("Show in its folder", row.ShowInFolderCommand);
        if (Projects?.Detail?.Task.Id != row.Task.Id)
            Item("Open the task", row.OpenTaskCommand);
        if (row.CanBeCover)
            Item("Set as cover", row.MakeCoverCommand);
        if (row.IsCover)
            Item("Remove cover", row.RemoveCoverCommand);
        items.Add(new Separator());
        Item("Remove the file", row.RemoveCommand);
        new ContextMenu { ItemsSource = items }.Open(sender as Control);
        e.Handled = true;
    }

    private void DescriptionBold(object? sender, RoutedEventArgs e) => DescriptionEditor.ToggleBold();
    private void DescriptionItalic(object? sender, RoutedEventArgs e) => DescriptionEditor.ToggleItalic();
    private void DescriptionUnderline(object? sender, RoutedEventArgs e) => DescriptionEditor.ToggleUnderline();
    private void DescriptionBullets(object? sender, RoutedEventArgs e) => DescriptionEditor.ToggleList(Webignite.Core.Writing.ListKind.Bullet);
    private void DescriptionNumbers(object? sender, RoutedEventArgs e) => DescriptionEditor.ToggleList(Webignite.Core.Writing.ListKind.Number);

    /// <summary>The templates to start a project from, then removing one.</summary>
    private void TemplatesOpening(object? sender, EventArgs e)
    {
        if (Projects is not { } projects || sender is not MenuFlyout flyout)
            return;
        flyout.Items.Clear();
        var gallery = new MenuItem { Header = "Template gallery…" };
        ToolTip.SetTip(gallery, "Start from one of the templates that come with the app, or one of your own");
        gallery.Click += (_, _) => projects.ShowTemplates();
        flyout.Items.Add(gallery);
        flyout.Items.Add(new Separator());
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
