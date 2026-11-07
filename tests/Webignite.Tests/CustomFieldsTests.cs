using System.Globalization;
using Webignite.Core.Projects;

namespace Webignite.Tests;

public class CustomFieldsTests
{
    // As Windows sets South Africa up: a comma for decimals and a space between thousands. ICU's copy differs, so pin it.
    private static readonly CultureInfo SouthAfrica = SouthAfricanWindows();

    private static CultureInfo SouthAfricanWindows()
    {
        var culture = (CultureInfo)new CultureInfo("en-ZA").Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = " ";
        return culture;
    }
    private static int ids;
    private static readonly string[] Reserved = ["None", "Red", "Green", "YellowGreen"];

    private static string NewId() => "id" + Interlocked.Increment(ref ids);

    private static Project Bosveld() => new() { Id = "p", Name = "Bosveld Builders website" };

    [Fact]
    public void A_choice_field_gets_its_options_coloured_and_names_must_be_new()
    {
        var (project, problem) = CustomFields.Add(Bosveld(), " Stage ", FieldKind.Choice, ["Not started", " Doing ", "Waiting on client", "Done", "doing", ""], NewId);
        Assert.Equal("", problem);
        var field = Assert.Single(project!.CustomFields);
        Assert.Equal("Stage", field.Name);
        Assert.Equal(["Not started", "Doing", "Waiting on client", "Done"], field.Options.Select(o => o.Name));
        Assert.Equal(4, field.Options.Select(o => o.Colour).Distinct().Count());
        Assert.All(field.Options, o => Assert.DoesNotContain(o.Colour, Reserved));
        Assert.All(field.Options, o => Assert.Contains(o.Colour, ProjectLooks.Colours));

        Assert.Contains("already", CustomFields.Add(project, "stage", FieldKind.Text, [], NewId).Problem);
        Assert.Contains("name", CustomFields.Add(project, " ", FieldKind.Text, [], NewId).Problem);
        Assert.Contains("option", CustomFields.Add(project, "Effort", FieldKind.Choice, [" "], NewId).Problem);
        Assert.Empty(CustomFields.Remove(project, field.Id).CustomFields);
    }

    [Fact]
    public void Values_are_kept_the_same_on_any_pc_and_shown_the_pcs_way()
    {
        var hours = new CustomField("h", "Hours", FieldKind.Number);
        Assert.Equal(("12.5", ""), CustomFields.Read(hours, "12,5", SouthAfrica));
        Assert.Equal(("1250", ""), CustomFields.Read(hours, "1 250", SouthAfrica));
        Assert.Contains("number", CustomFields.Read(hours, "lots", SouthAfrica).Problem);
        Assert.Equal("12,5", CustomFields.Show(hours, "12.5", SouthAfrica));

        var signOff = new CustomField("d", "Sign-off", FieldKind.Date);
        Assert.Equal(("2026-11-20", ""), CustomFields.Read(signOff, "2026-11-20", SouthAfrica));
        Assert.Equal("20 Nov 2026", CustomFields.Show(signOff, "2026-11-20", SouthAfrica));
        Assert.Contains("date", CustomFields.Read(signOff, "soon", SouthAfrica).Problem);

        var stage = new CustomField("s", "Stage", FieldKind.Choice) { Options = [new FieldOption("o1", "Doing", "Blue")] };
        Assert.Equal(("o1", ""), CustomFields.Read(stage, "doing", SouthAfrica));
        Assert.Equal("Doing", CustomFields.Show(stage, "o1", SouthAfrica));
        Assert.Contains("options", CustomFields.Read(stage, "Done", SouthAfrica).Problem);
        Assert.Equal((null, ""), CustomFields.Read(stage, "  ", SouthAfrica));
        Assert.Equal("", CustomFields.Show(stage, "gone", SouthAfrica));
    }

    [Fact]
    public void Thousands_separators_only_count_where_they_can_be()
    {
        var hours = new CustomField("h", "Hours", FieldKind.Number);
        var german = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal(("1.5", ""), CustomFields.Read(hours, "1.5", german));
        Assert.Equal(("1.5", ""), CustomFields.Read(hours, "1,5", german));
        Assert.Equal(("1500", ""), CustomFields.Read(hours, "1.500", german));
        Assert.Equal(("1234567.25", ""), CustomFields.Read(hours, "1.234.567,25", german));
        Assert.Equal(("-2.125", ""), CustomFields.Read(hours, "-2,125", german));
        Assert.Equal(("12.5", ""), CustomFields.Read(hours, "12.5", german));
        // On a PC grouping with commas, "1,5" could be either: it isn't read as 15.
        var american = CultureInfo.GetCultureInfo("en-US");
        Assert.Contains("number", CustomFields.Read(hours, "1,5", american).Problem);
        Assert.Equal(("1250.5", ""), CustomFields.Read(hours, "1,250.5", american));
        Assert.Equal(("1250", ""), CustomFields.Read(hours, "1 250", SouthAfrica));
        Assert.Contains("number", CustomFields.Read(hours, "12 50", SouthAfrica).Problem);
    }

    [Fact]
    public void A_number_opens_to_change_in_full_not_rounded()
    {
        var hours = new CustomField("h", "Hours", FieldKind.Number);
        Assert.Equal("2,13", CustomFields.Show(hours, "2.125", SouthAfrica));
        Assert.Equal("2,125", CustomFields.Editing(hours, "2.125", SouthAfrica));
        Assert.Equal("1250", CustomFields.Editing(hours, "1250", SouthAfrica));
        Assert.Equal(("2.125", ""), CustomFields.Read(hours, CustomFields.Editing(hours, "2.125", SouthAfrica), SouthAfrica));
        Assert.Equal("", CustomFields.Editing(hours, null, SouthAfrica));
        Assert.Equal("2026-11-20", CustomFields.Editing(new CustomField("d", "Sign-off", FieldKind.Date), "2026-11-20", SouthAfrica));
        var stage = new CustomField("s", "Stage", FieldKind.Choice) { Options = [new FieldOption("o1", "Doing", "Blue")] };
        Assert.Equal("Doing", CustomFields.Editing(stage, "o1", SouthAfrica));
    }

    [Fact]
    public void Undo_puts_back_only_the_field_taken_off_where_it_was()
    {
        var stage = new CustomField("s", "Stage", FieldKind.Choice);
        var hours = new CustomField("h", "Hours", FieldKind.Number);
        var notes = new CustomField("n", "Notes", FieldKind.Text);
        var removed = CustomFields.Remove(Bosveld() with { CustomFields = [stage, hours, notes] }, "h");
        // Meanwhile a field was added and another renamed: those stay.
        var since = removed with { CustomFields = [stage with { Name = "Phase" }, notes, new CustomField("e", "Effort", FieldKind.Number)] };
        var back = CustomFields.PutBack(since, hours, 1);
        Assert.Equal(["Phase", "Hours", "Notes", "Effort"], back.CustomFields.Select(f => f.Name));
        Assert.Same(back, CustomFields.PutBack(back, hours, 1));
        Assert.Equal(["Hours"], CustomFields.PutBack(Bosveld(), hours, 5).CustomFields.Select(f => f.Name));
    }

    [Fact]
    public void A_task_sets_and_clears_a_value()
    {
        var task = new ProjectTask { Id = "t", ProjectId = "p", Title = "Wireframes" };
        task = CustomFields.WithValue(task, "h", "3");
        task = CustomFields.WithValue(task, "s", "o1");
        Assert.Equal("3", CustomFields.ValueOf(task, "h"));
        task = CustomFields.WithValue(task, "h", null);
        Assert.Null(CustomFields.ValueOf(task, "h"));
        Assert.Equal(["s"], task.Values.Keys);
    }
}
