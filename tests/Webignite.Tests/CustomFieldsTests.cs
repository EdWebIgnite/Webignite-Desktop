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
