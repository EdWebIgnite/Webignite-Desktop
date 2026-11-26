using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Projects;

namespace Webignite.Tests;

public class DocumentFieldsTests
{
    private static Document Doc(params DocumentField[] fields) =>
        new() { Kind = DocumentKind.Invoice, Origin = DocumentOrigin.New, Customer = new Customer { Name = "Trimax" }, Lines = [], Fields = fields };

    [Fact]
    public void A_field_is_set_changed_and_taken_off_by_name()
    {
        var doc = DocumentFields.With(Doc(), "Project", " Spar Brits ");
        Assert.Equal("Spar Brits", DocumentFields.Value(doc, "project"));
        doc = DocumentFields.With(doc, "PROJECT", "Spar Hartbeespoort");
        Assert.Single(doc.Fields);
        Assert.Empty(DocumentFields.With(doc, "Project", "").Fields);
    }

    [Fact]
    public void Only_printed_fields_go_to_the_client_and_to_Xeros_reference()
    {
        var doc = Doc(new DocumentField("Project", "Spar Brits"), new DocumentField("Our cost", "R 4 000", Print: false)) with { Reference = "Website November" };
        Assert.Equal(["Project"], DocumentFields.Printed(doc).Select(f => f.Name));
        Assert.Equal("Website November · Project: Spar Brits", DocumentFields.XeroReference(doc));
        Assert.Equal("Project: Spar Brits", DocumentFields.XeroReference(doc with { Reference = null }));
    }

    [Fact]
    public void Fields_to_fill_follow_settings_and_keep_ones_no_longer_offered()
    {
        var doc = Doc(new DocumentField("PO number", "4410"));
        var fill = DocumentFields.ToFill(DocumentFields.Usual, doc);
        Assert.Equal([("Project", ""), ("PO number", "4410")], fill.Select(f => (f.Field.Name, f.Value)));
    }

    [Fact]
    public void A_new_field_needs_a_name_of_its_own()
    {
        Assert.NotNull(DocumentFields.Problem(DocumentFields.Usual, " "));
        Assert.NotNull(DocumentFields.Problem(DocumentFields.Usual, "project"));
        Assert.NotNull(DocumentFields.Problem(DocumentFields.Usual, "Reference"));
        Assert.Null(DocumentFields.Problem(DocumentFields.Usual, "PO number"));
    }

    [Fact]
    public void Project_choices_are_the_clients_running_projects_and_planner_projects_once()
    {
        var client = new Client
        {
            Id = "t", Name = "Trimax",
            Projects = [new ClientProject { Id = "1", Name = "Spar Brits" }, new ClientProject { Id = "2", Name = "Old site", Finished = true }],
        };
        Project[] planner = [
            new() { Id = "a", Name = "spar brits", ClientId = "t" },
            new() { Id = "b", Name = "Pick n Pay Rustenburg", ClientId = "t" },
            new() { Id = "c", Name = "Done one", ClientId = "t", Status = ProjectStatus.Done },
            new() { Id = "d", Name = "Someone else's", ClientId = "x" },
        ];
        Assert.Equal(["Spar Brits", "Pick n Pay Rustenburg"], DocumentFields.ProjectChoices(client, planner));
        Assert.Empty(DocumentFields.ProjectChoices(null, planner));
    }
}
