using Webignite.Core.Clients;

namespace Webignite.Tests;

/// <summary>People to and from vCard and CSV files, and bringing them into Contacts.</summary>
public sealed class ContactFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.FromHours(2));

    private static Person Thandi() => new()
    {
        Id = "p1",
        Name = "Thandi Mokoena",
        Role = "Owner",
        ClientId = "c1",
        Emails = [new ContactWay("Work", "thandi@karoocoffee.co.za"), new ContactWay("Personal", "thandi@gmail.com")],
        Phones = [new ContactWay("Mobile", "082 555 0100")],
        Groups = ["Hosting clients", "Newsletter"],
        Notes = "Prefers WhatsApp; mornings, please",
    };

    [Fact]
    public void A_vCard_written_here_reads_back_the_same()
    {
        var card = ContactFiles.ToVCard([Thandi()], id => id == "c1" ? "Karoo Coffee Roasters" : null);
        Assert.Contains("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Thandi Mokoena\r\n", card, StringComparison.Ordinal);
        var back = Assert.Single(ContactFiles.FromVCard(card, Now));
        Assert.Equal(("Thandi Mokoena", "Owner", "Karoo Coffee Roasters", "Prefers WhatsApp; mornings, please"), (back.Name, back.Role, back.Company, back.Notes));
        Assert.Equal(["thandi@karoocoffee.co.za", "thandi@gmail.com"], back.Emails.Select(e => e.Value));
        Assert.Equal(("Personal", "Mobile"), (back.Emails[1].Label, back.Phones[0].Label));
        Assert.Equal(["Hosting clients", "Newsletter"], back.Groups);
    }

    [Fact]
    public void A_phones_vCard_with_folded_lines_and_quoted_printable_is_read()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:2.1\r\nN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Dlamini;L=C3=A9rato;;;\r\nTEL;CELL:+27 82 555\r\n 0142\r\nitem1.EMAIL;TYPE=INTERNET:lerato@fynbosphysio.co.za\r\nEND:VCARD\r\nBEGIN:VCARD\r\nVERSION:4.0\r\nEMAIL:only@address.co.za\r\nEND:VCARD\r\n";
        var people = ContactFiles.FromVCard(card, Now);
        Assert.Equal(2, people.Count);
        Assert.Equal(("Lérato Dlamini", "+27 82 5550142", "lerato@fynbosphysio.co.za"), (people[0].Name, people[0].Phone, people[0].Email));
        Assert.Equal("only@address.co.za", people[1].Name);
    }

    [Fact]
    public void A_CSV_written_here_reads_back_and_is_safe_in_a_spreadsheet()
    {
        var formula = Thandi() with { Notes = "=HYPERLINK(\"x\")" };
        var csv = ContactFiles.ToCsv([formula], _ => "Karoo Coffee Roasters");
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv, StringComparison.Ordinal);
        var back = Assert.Single(ContactFiles.FromCsv(ContactFiles.ToCsv([Thandi()], _ => null), Now));
        Assert.Equal(["thandi@karoocoffee.co.za", "thandi@gmail.com"], back.Emails.Select(e => e.Value));
        Assert.Equal(["Hosting clients", "Newsletter"], back.Groups);
        Assert.Equal("Prefers WhatsApp; mornings, please", back.Notes);
    }

    [Fact]
    public void Outlook_and_Google_CSV_exports_are_read_by_their_headers()
    {
        const string outlook = "First Name,Last Name,Company,Job Title,E-mail Address,E-mail 2 Address,Mobile Phone,Categories\r\nPieter,van Wyk,Bosveld Builders,Owner,pieter@bosveldbuilders.co.za,,082 555 0142,Hosting clients\r\n";
        var pieter = Assert.Single(ContactFiles.FromCsv(outlook, Now));
        Assert.Equal(("Pieter van Wyk", "Bosveld Builders", "Owner", "pieter@bosveldbuilders.co.za", "082 555 0142"),
            (pieter.Name, pieter.Company, pieter.Role, pieter.Email, pieter.Phone));
        Assert.Equal(["Hosting clients"], pieter.Groups);

        const string google = "Name;Organization 1 - Name;E-mail 1 - Value;Phone 1 - Value;Group Membership\r\nLerato Dlamini;Fynbos Physio;lerato@fynbosphysio.co.za ::: admin@fynbosphysio.co.za;021 555 0199;* myContacts ::: Newsletter\r\n";
        var lerato = Assert.Single(ContactFiles.FromCsv(google, Now));
        Assert.Equal(2, lerato.Emails.Count);
        Assert.Equal(["Newsletter"], lerato.Groups);
    }

    [Fact]
    public void Importing_skips_people_already_here_and_links_their_client()
    {
        var karoo = new Client { Id = "c1", Name = "Karoo Coffee Roasters" };
        var existing = new[] { Thandi() };
        var imported = new[]
        {
            new Person { Id = "x1", Name = "T Mokoena", Emails = [new ContactWay("Work", "THANDI@karoocoffee.co.za")] },
            new Person { Id = "x2", Name = "Sipho Ndlovu", Company = "karoo coffee roasters", Emails = [new ContactWay("Work", "sipho@karoocoffee.co.za")] },
            new Person { Id = "x3", Name = "Sipho N", Emails = [new ContactWay("Work", "sipho@karoocoffee.co.za")] },
        };
        var plan = ContactFiles.Plan(existing, imported, [karoo]);
        var sipho = Assert.Single(plan.ToAdd);
        Assert.Equal(("Sipho Ndlovu", "c1"), (sipho.Name, sipho.ClientId));
        Assert.Equal(2, plan.AlreadyHere);
        Assert.Equal("Added 1 person. 2 were in Contacts already.", ContactFiles.Summary(plan));
    }
}
