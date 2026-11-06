using Webignite.Core.Projects;

namespace Webignite.Tests;

/// <summary>The templates that come with the app, for the gallery.</summary>
public sealed class BuiltInTemplatesTests
{
    [Fact]
    public void Every_template_is_whole_and_files_its_tasks_under_its_own_headings()
    {
        Assert.Equal(11, BuiltInTemplates.All.Count);
        Assert.Equal(BuiltInTemplates.All.Count, BuiltInTemplates.All.Select(t => t.Id).Distinct().Count());
        foreach (var template in BuiltInTemplates.All)
        {
            Assert.NotEmpty(template.Template.Headings);
            Assert.NotEmpty(template.Template.Tasks);
            Assert.All(template.Template.Tasks, t => Assert.Contains(t.Heading, template.Template.Headings));
            Assert.Contains(template.Icon, ProjectLooks.Icons);
            Assert.Contains(template.Colour, ProjectLooks.Colours);
            Assert.Contains(template.Category, BuiltInTemplates.Categories);
            Assert.False(string.IsNullOrWhiteSpace(template.Description));
            Assert.DoesNotContain('—', template.Description);
        }
    }

    [Fact]
    public void A_project_from_a_built_in_template_dates_its_tasks_from_the_start()
    {
        var web = BuiltInTemplates.Find("builtin-web-design")!;
        var start = new DateOnly(2026, 11, 9);
        var (project, headings, tasks) = ProjectRules.FromTemplate(web.Template, "Karoo Coffee website", start, 3);
        Assert.Equal("Karoo Coffee website", project.Name);
        Assert.Equal(web.Template.Headings, headings.OrderBy(h => h.Order).Select(h => h.Name));
        var kickOff = tasks.Single(t => t.Title == "Kick-off call with the client");
        Assert.Equal(start.AddDays(2), kickOff.Due);
        Assert.Contains(tasks, t => t.ParentId is not null);
        Assert.All(tasks, t => Assert.False(t.Done));
        Assert.Equal(["Design", "Marketing", "Operations & PMO"], BuiltInTemplates.Categories.Where(c => c is "Design" or "Marketing" or "Operations & PMO").Order());
        Assert.Equal(4, BuiltInTemplates.In(BuiltInTemplates.Operations).Count);
    }
}
