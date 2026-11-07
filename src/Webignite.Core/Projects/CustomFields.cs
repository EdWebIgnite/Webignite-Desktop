using System.Globalization;

namespace Webignite.Core.Projects;

/// <summary>What a custom field holds, as Asana's: a pick from a list, words, a number or a date.</summary>
public enum FieldKind { Choice, Text, Number, Date }

/// <summary>One of a choice field's options, with its colour from the project colours.</summary>
public sealed record FieldOption(string Id, string Name, string Colour);

/// <summary>A field a project adds to its tasks, as Asana's custom fields: Stage, Effort, Hours, Sign-off date.</summary>
public sealed record CustomField(string Id, string Name, FieldKind Kind)
{
    public IReadOnlyList<FieldOption> Options { get; init; } = [];
}

/// <summary>
/// The rules for custom fields. A task keeps its values by field id as text that doesn't depend on the PC's
/// region (numbers with a full stop, dates as yyyy-MM-dd, a choice as its option's id); they're shown the PC's way.
/// </summary>
public static class CustomFields
{
    public static string Label(FieldKind kind) => kind switch
    {
        FieldKind.Choice => "Choice from a list",
        FieldKind.Text => "Text",
        FieldKind.Number => "Number",
        _ => "Date",
    };

    /// <summary>
    /// The colours options take in turn, from the project colours: well apart from each other, and never red or
    /// green, which mean a problem and done everywhere else in the app.
    /// </summary>
    public static IReadOnlyList<string> OptionColours { get; } =
        ["Blue", "Purple", "Yellow", "Aqua", "Orange", "Magenta", "Indigo", "BlueGreen", "HotPink", "YellowOrange", "Pink", "CoolGrey"];

    /// <summary>A new field on the project, its options coloured in turn; says what's wrong when it can't be added.</summary>
    public static (Project? Changed, string Problem) Add(Project project, string name, FieldKind kind, IEnumerable<string> options, Func<string> newId)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return (null, "Give the field a name.");
        if (project.CustomFields.Any(f => string.Equals(f.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            return (null, $"There's already a field called {trimmed}.");
        var names = options.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (kind == FieldKind.Choice && names.Count == 0)
            return (null, "Give the choice at least one option, e.g. Not started, Doing, Done.");
        var field = new CustomField(newId(), trimmed, kind)
        {
            Options = kind == FieldKind.Choice ? [.. names.Select((o, i) => new FieldOption(newId(), o, OptionColours[i % OptionColours.Count]))] : [],
        };
        return (project with { CustomFields = [.. project.CustomFields, field] }, "");
    }

    public static Project Remove(Project project, string fieldId) => project with { CustomFields = [.. project.CustomFields.Where(f => f.Id != fieldId)] };

    /// <summary>
    /// A field taken off, put back where it was, as Undo does. Only that field: fields added, changed or taken off
    /// since stay as they are now, and nothing changes when it's already back.
    /// </summary>
    public static Project PutBack(Project project, CustomField field, int index)
    {
        if (project.CustomFields.Any(f => f.Id == field.Id))
            return project;
        var fields = project.CustomFields.ToList();
        fields.Insert(Math.Clamp(index, 0, fields.Count), field);
        return project with { CustomFields = fields };
    }

    /// <summary>
    /// What's typed for a field, as it's kept: null clears it. A number or date that can't be read, or a choice
    /// that isn't one of the field's options, comes back with what's wrong.
    /// </summary>
    public static (string? Value, string Problem) Read(CustomField field, string typed, IFormatProvider culture)
    {
        var text = typed.Trim();
        if (text.Length == 0)
            return (null, "");
        switch (field.Kind)
        {
            case FieldKind.Number:
                return decimal.TryParse(text, NumberStyles.Number, culture, out var number) || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number)
                    ? (number.ToString(CultureInfo.InvariantCulture), "")
                    : (null, $"{field.Name} takes a number.");
            case FieldKind.Date:
                return DateOnly.TryParse(text, culture, DateTimeStyles.None, out var date) || DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                    ? (date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "")
                    : (null, $"{field.Name} takes a date.");
            case FieldKind.Choice:
                return field.Options.FirstOrDefault(o => o.Id == text || string.Equals(o.Name, text, StringComparison.OrdinalIgnoreCase)) is { } option
                    ? (option.Id, "")
                    : (null, $"That isn't one of {field.Name}'s options.");
            default:
                return (text, "");
        }
    }

    /// <summary>A kept value as it shows: a number and a date the PC's way, a choice as its option's name; empty when it's not set.</summary>
    public static string Show(CustomField field, string? value, IFormatProvider culture)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return field.Kind switch
        {
            FieldKind.Number => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number.ToString("#,0.##", culture) : value,
            FieldKind.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.ToString("d MMM yyyy", culture) : value,
            FieldKind.Choice => Option(field, value)?.Name ?? "",
            _ => value,
        };
    }

    public static FieldOption? Option(CustomField field, string? value) => field.Options.FirstOrDefault(o => o.Id == value);

    /// <summary>The task with a field's value set, or taken off when null.</summary>
    public static ProjectTask WithValue(ProjectTask task, string fieldId, string? value)
    {
        var values = task.Values.Where(v => v.Key != fieldId).ToDictionary(v => v.Key, v => v.Value);
        if (value is not null)
            values[fieldId] = value;
        return task with { Values = values.OrderBy(v => v.Key, StringComparer.Ordinal).ToDictionary(v => v.Key, v => v.Value) };
    }

    public static string? ValueOf(ProjectTask task, string fieldId) => task.Values.GetValueOrDefault(fieldId);
}
