using Webignite.Core.Clients;
using Webignite.Core.Mail;
using Webignite.Core.Projects;

namespace Webignite.Tests;

public sealed class MailProjectsTests
{
    [Fact]
    public void A_project_is_kept_on_the_mail_as_a_keyword_holding_its_id()
    {
        var keyword = MailProjects.Keyword("0193abc");
        var mail = new MailSummary { AccountId = "a", Folder = "INBOX", Uid = 1, Keywords = ["waiting", keyword, keyword.ToUpperInvariant()] };

        Assert.Equal("0193abc", MailProjects.ProjectId(keyword));
        Assert.Null(MailProjects.ProjectId("waiting"));
        Assert.Equal(["0193abc"], MailProjects.Of(mail));
    }

    [Fact]
    public void Projects_to_file_under_are_grouped_by_client_with_loose_ones_last_and_finished_ones_left_out()
    {
        var clients = new[] { new Client { Id = "z", Name = "Zebra Studio" }, new Client { Id = "a", Name = "Atlas Design" } };
        var projects = new[]
        {
            new Project { Id = "1", Name = "Website", ClientId = "z" },
            new Project { Id = "2", Name = "Shop", ClientId = "a", For = "Karoo Coffee" },
            new Project { Id = "3", Name = "Old site", ClientId = "a", Status = ProjectStatus.Done },
            new Project { Id = "4", Name = "Our own SEO" },
        };

        var groups = MailProjects.Groups(projects, clients);

        Assert.Equal(["Atlas Design", "Zebra Studio", "Not for a client"], groups.Select(g => g.Client));
        Assert.Equal(["2"], groups[0].Projects.Select(p => p.Id));
        Assert.Equal(["4"], groups[2].Projects.Select(p => p.Id));
        Assert.Equal(["Zebra Studio"], MailProjects.Groups(projects, clients, p => p.Id == "1").Select(g => g.Client));
    }

    [Fact]
    public void Projects_are_put_on_Mails_list_and_taken_off_by_hand()
    {
        var settings = new MailProjectSettings().With("a", true).With("b", true).With("a", true);
        Assert.Equal(["a", "b"], settings.Shown);
        Assert.Equal(["b"], settings.With("a", false).Shown);
    }
}
