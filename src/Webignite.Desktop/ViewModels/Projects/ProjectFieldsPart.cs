using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// Custom fields, as Asana's: a project adds its own fields to its tasks (a choice with coloured options, text, a
// number or a date). They show as columns in the list, as pills on board cards, and as rows in the task pane.
public sealed partial class ProjectFieldsPart(AppServices services, ProjectsViewModel page) : ViewModelBase
{
    /// <summary>The open project's custom fields, as column headings in the list.</summary>
    public ObservableCollection<FieldHeader> FieldHeaders { get; } = [];

    public static IReadOnlyList<Choice<FieldKind>> FieldKinds { get; } =
        [.. Enum.GetValues<FieldKind>().Select(k => new Choice<FieldKind>(CustomFields.Label(k), k))];

    [ObservableProperty] public partial string NewFieldName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewFieldIsChoice))]
    public partial Choice<FieldKind> NewFieldKind { get; set; } = FieldKinds[0];

    public bool NewFieldIsChoice => NewFieldKind.Value == FieldKind.Choice;

    [ObservableProperty] public partial string NewFieldOptions { get; set; } = "";

    private IReadOnlyList<CustomField> OpenFields => page.open?.CustomFields ?? [];

    internal void LoadFieldHeaders()
    {
        FieldHeaders.Clear();
        foreach (var field in OpenFields)
            FieldHeaders.Add(new FieldHeader(field, page));
    }

    /// <summary>A row's cells for the project's fields, in their order.</summary>
    internal IReadOnlyList<FieldCell> CellsFor(ProjectTask task) =>
        [.. OpenFields.Select(f => new FieldCell(f, task.Id, CustomFields.ValueOf(task, f.Id), page))];

    /// <summary>Adds the field described in the form; a choice takes its options from a list split by commas.</summary>
    [RelayCommand]
    private void AddField()
    {
        if (page.open is null)
            return;
        var options = NewFieldOptions.Split([',', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var (saved, problems) = page.Ours(() => services.Projects.AddField(page.open.Id, NewFieldName, NewFieldKind.Value, options));
        page.Message = saved is null ? string.Join(" ", problems) : "";
        if (saved is null)
            return;
        page.open = saved;
        NewFieldName = "";
        NewFieldOptions = "";
        NewFieldKind = FieldKinds[0];
        page.ShowTasks();
        page.Detail?.Reload();
    }

    internal void RemoveField(FieldHeader header)
    {
        if (page.open is null)
            return;
        var field = header.Field;
        var index = page.open.CustomFields.ToList().FindIndex(f => f.Id == field.Id);
        var projectId = page.open.Id;
        var (saved, _) = page.Ours(() => services.Projects.RemoveField(projectId, field.Id));
        if (saved is null)
            return;
        page.open = saved;
        page.ShowTasks();
        page.Detail?.Reload();
        // Undo puts back only this field, where it was; fields added or changed since stay as they are.
        services.OfferUndo($"Removed the field {header.Name}", () =>
        {
            services.Projects.Change(projectId, p => CustomFields.PutBack(p, field, index));
            page.Refresh();
        });
    }

    /// <summary>A task's value for a field, from what's typed or picked; empty clears it.</summary>
    public void SetFieldValue(string taskId, string fieldId, string typed)
    {
        var (saved, problems) = page.Ours(() => services.Projects.SetFieldValue(taskId, fieldId, typed, CultureInfo.CurrentCulture));
        page.Message = saved is null ? string.Join(" ", problems) : "";
        if (saved is null)
            return;
        page.ShowTasks();
        if (page.Detail is { } detail && detail.Task.Id == taskId)
            detail.Reload();
    }
}

/// <summary>A custom field's heading in the list: its name, and a menu to take it off.</summary>
public sealed partial class FieldHeader(CustomField field, ProjectsViewModel page) : ObservableObject
{
    public CustomField Field { get; } = field;
    public string Name => Field.Name;
    public string KindText => CustomFields.Label(Field.Kind);

    [RelayCommand]
    private void Remove() => page.FieldsPart.RemoveField(this);
}

/// <summary>A task's value for one field, as a cell in the list or a pill on a card; a click changes it.</summary>
public sealed class FieldCell(CustomField field, string taskId, string? value, ProjectsViewModel page)
{
    public CustomField Field { get; } = field;
    public string TaskId { get; } = taskId;
    public string? Value { get; } = value;
    public ProjectsViewModel Page { get; } = page;
    public string Text => CustomFields.Show(Field, Value, CultureInfo.CurrentCulture);
    public bool HasValue => Text.Length > 0;
    public bool IsPill => Field.Kind == FieldKind.Choice && HasValue;
    public bool IsPlain => !IsPill;
    public string Colour => CustomFields.Option(Field, Value)?.Colour ?? "None";

    /// <summary>What a box to change it starts with: a number in full the PC's way, a date as it's kept, else as it shows.</summary>
    public string Typed => CustomFields.Editing(Field, Value, CultureInfo.CurrentCulture);
}

/// <summary>A custom field in the task pane: a list to pick from, or a box to type in, saved as soon as it changes.</summary>
public sealed partial class FieldEditor : ObservableObject
{
    private readonly string taskId;
    private readonly ProjectsViewModel page;
    private readonly bool loading;
    private readonly string loadedText;

    public FieldEditor(CustomField field, string taskId, string? value, ProjectsViewModel page)
    {
        Field = field;
        this.taskId = taskId;
        this.page = page;
        loading = true;
        Options = [new Choice<string?>("None", null), .. field.Options.Select(o => new Choice<string?>(o.Name, o.Id))];
        Picked = Options.FirstOrDefault(o => o.Value == value) ?? Options[0];
        Text = field.Kind is FieldKind.Text or FieldKind.Number ? CustomFields.Editing(field, value, CultureInfo.CurrentCulture) : "";
        loadedText = Text;
        Date = field.Kind == FieldKind.Date && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToDateTime(TimeOnly.MinValue) : null;
        loading = false;
    }

    public CustomField Field { get; }
    public string Name => Field.Name;
    public bool IsChoice => Field.Kind == FieldKind.Choice;
    public bool IsDate => Field.Kind == FieldKind.Date;
    public bool IsTyped => Field.Kind is FieldKind.Text or FieldKind.Number;
    public string Hint => Field.Kind == FieldKind.Number ? "A number" : "Type here";
    public IReadOnlyList<Choice<string?>> Options { get; }

    [ObservableProperty] public partial Choice<string?>? Picked { get; set; }
    [ObservableProperty] public partial string Text { get; set; }
    [ObservableProperty] public partial DateTime? Date { get; set; }

    partial void OnPickedChanged(Choice<string?>? value)
    {
        if (!loading)
            page.FieldsPart.SetFieldValue(taskId, Field.Id, value?.Value ?? "");
    }

    partial void OnDateChanged(DateTime? value)
    {
        if (!loading)
            page.FieldsPart.SetFieldValue(taskId, Field.Id, value is { } d ? DateOnly.FromDateTime(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "");
    }

    /// <summary>Saves what's typed, when the box is left or Enter is pressed.</summary>
    [RelayCommand]
    private void Save()
    {
        if (Text != loadedText)
            page.FieldsPart.SetFieldValue(taskId, Field.Id, Text);
    }
}

public sealed partial class TaskDetail
{
    /// <summary>The project's custom fields for this task, as rows under the usual ones.</summary>
    public ObservableCollection<FieldEditor> FieldEditors { get; } = [];

    public bool HasFieldEditors => FieldEditors.Count > 0;

    private void LoadFieldEditors()
    {
        FieldEditors.Clear();
        foreach (var field in services.Projects.Get(Task.ProjectId)?.CustomFields ?? [])
            FieldEditors.Add(new FieldEditor(field, Task.Id, CustomFields.ValueOf(Task, field.Id), page));
        OnPropertyChanged(nameof(HasFieldEditors));
    }
}
