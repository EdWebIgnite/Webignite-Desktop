using Webignite.Core.Clients;
using Webignite.Core.Mail;
using Webignite.Core.Writing;

namespace Webignite.Tests;

/// <summary>Mail templates, and adding people from Contacts to a mail.</summary>
public sealed class MailTemplateTests
{
    private static readonly IReadOnlyList<Block> Signature = RichText.Signature("Eddie\nWebignite", null, null);

    private static Person Someone(string name, string? email, string? clientId = null, params string[] groups) => new()
    {
        Id = Person.NewId(),
        Name = name,
        ClientId = clientId,
        Groups = groups,
        Emails = email is null ? [] : [new ContactWay("Work", email)],
    };

    [Fact]
    public void A_template_is_kept_without_the_signature_or_empty_lines_at_the_end()
    {
        var writing = RichText.Starting("Hi {name},\n\nYour site was updated this month.\n\n", Signature);
        var template = MailTemplates.From(" Monthly report ", "thandi@karoocoffee.co.za", "", " Your {client} site ", writing, "t1");

        Assert.Equal("Monthly report", template.Name);
        Assert.Equal("Your {client} site", template.Subject);
        Assert.DoesNotContain("Webignite", template.Html);
        Assert.Contains("Your site was updated this month.", template.Html);
        Assert.True(writing.HasSignature); // the mail being written keeps its own
        var back = RichHtml.Read(template.Html, RichHtml.DataAddress);
        Assert.Equal("Your site was updated this month.", ((TextBlock)back.Blocks[^1]).Text);
    }

    [Fact]
    public void Using_a_template_fills_in_the_name_and_client_and_adds_the_mailbox_signature()
    {
        var template = MailTemplates.From("Report", "", "", "Your {client} site", RichText.FromText("Hi {name},\n\nAll done."), "t1");

        var writing = MailTemplates.Writing(template, "Thandi", "Karoo Coffee Roasters", Signature);
        var text = RichText.ToText(writing);

        Assert.StartsWith("Hi Thandi,", text);
        Assert.Contains("All done.", text);
        Assert.Contains("Eddie", text);
        Assert.True(writing.HasSignature);
        Assert.Equal("Your Karoo Coffee Roasters site", MailTemplates.Subject(template, "Thandi", "Karoo Coffee Roasters"));
        Assert.False(MailTemplates.Writing(template, null, null, []).HasSignature);
    }

    [Fact]
    public void A_template_needs_a_name_of_its_own_and_something_written()
    {
        var existing = new[] { new MailTemplate("t1", "Monthly report", "", "", "", "") };
        var written = RichText.FromText("Hello");

        Assert.Empty(MailTemplates.Check("Welcome", written, "", existing));
        Assert.Single(MailTemplates.Check(" ", written, "", existing));
        Assert.Single(MailTemplates.Check("monthly REPORT", written, "", existing));
        Assert.Single(MailTemplates.Check("Welcome", RichText.Starting(null, Signature), " ", existing));
        Assert.Empty(MailTemplates.Check("Welcome", RichText.Starting(null, Signature), "Just a subject", existing));
    }

    [Fact]
    public void Adding_people_to_a_box_skips_anyone_already_in_it()
    {
        var thandi = new MailPerson("Thandi Mokoena", "thandi@karoocoffee.co.za");
        var sipho = new MailPerson("Sipho Ndlovu", "sipho@karoocoffee.co.za");

        Assert.Equal("Thandi Mokoena <thandi@karoocoffee.co.za>, ", AddressBook.Add("", [thandi]));
        Assert.Equal("Thandi Mokoena <thandi@karoocoffee.co.za>, Sipho Ndlovu <sipho@karoocoffee.co.za>, ",
            AddressBook.Add("Thandi Mokoena <thandi@karoocoffee.co.za>, ", [thandi, sipho]));
        Assert.Equal("THANDI@karoocoffee.co.za", AddressBook.Add("THANDI@karoocoffee.co.za", [thandi]));
    }

    [Fact]
    public void The_contacts_list_in_a_mail_has_groups_first_then_people_with_an_address()
    {
        var people = new[]
        {
            Someone("Zanele Dube", "zanele@fynbosphysio.co.za", "c2", "Monthly report"),
            Someone("Thandi Mokoena", "thandi@karoocoffee.co.za", "c1", "Monthly report"),
            Someone("No Address", null, null, "Monthly report"),
        };
        var clients = new Dictionary<string, string> { ["c1"] = "Karoo Coffee Roasters", ["c2"] = "Fynbos Physio" };

        var all = AddressBook.Pick(people, clients, "");
        Assert.Equal(["Monthly report", "Thandi Mokoena", "Zanele Dube"], all.Select(c => c.Name));
        Assert.Equal(2, all[0].Members!.Count);

        var karoo = AddressBook.Pick(people, clients, "karoo");
        Assert.Equal("Thandi Mokoena", Assert.Single(karoo).Name);
        // A group's name finds the group and the people in it.
        Assert.Equal(["Monthly report", "Thandi Mokoena", "Zanele Dube"], AddressBook.Pick(people, clients, "monthly").Select(c => c.Name));
    }
}
