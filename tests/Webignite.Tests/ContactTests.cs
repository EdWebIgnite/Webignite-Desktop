using System.Security.Cryptography;
using Webignite.Core.Clients;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>People in Contacts, and how they stay in step with the clients they're with.</summary>
public sealed class ContactTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-contacts-").FullName;
    private readonly LocalStore store;
    private readonly ClientService clients;
    private readonly ContactService contacts;

    public ContactTests()
    {
        store = new LocalStore(Path.Combine(folder, "webignite.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        clients = new ClientService(store);
        contacts = new ContactService(store);
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private static Person Someone(string name, string? email = null, string? phone = null, string? clientId = null) => new()
    {
        Id = Person.NewId(),
        Name = name,
        ClientId = clientId,
        Emails = email is null ? [] : [new ContactWay("Work", email)],
        Phones = phone is null ? [] : [new ContactWay("Mobile", phone)],
    };

    [Fact]
    public void A_contact_needs_a_name_and_real_addresses_and_is_tidied()
    {
        Assert.Equal(2, ContactRules.Check(Someone(" ", "not an address")).Count);
        var tidy = ContactRules.Tidy(Someone(" Thandi ", " thandi@karoocoffee.co.za ") with
        {
            Phones = [new ContactWay("Mobile", " ")],
            Groups = ["Hosting clients, newsletter", "hosting CLIENTS"],
        });
        Assert.Equal(("Thandi", "thandi@karoocoffee.co.za", 0), (tidy.Name, tidy.Email, tidy.Phones.Count));
        Assert.Equal(["Hosting clients", "newsletter"], tidy.Groups);
    }

    [Fact]
    public void Search_finds_a_person_by_any_detail_and_their_client()
    {
        var thandi = Someone("Thandi Mokoena", "thandi@karoocoffee.co.za", "082 555 0100") with { Role = "Owner", Groups = ["Hosting clients"] };
        Assert.True(ContactRules.Matches(thandi, "owner karoo"));
        Assert.True(ContactRules.Matches(thandi, "0100"));
        Assert.True(ContactRules.Matches(thandi, "hosting"));
        Assert.True(ContactRules.Matches(thandi, "roasters", clientName: "Karoo Coffee Roasters"));
        Assert.False(ContactRules.Matches(thandi, "bosveld"));
    }

    [Fact]
    public void Clients_from_before_contacts_get_their_person_once()
    {
        var karoo = clients.Add("Karoo Coffee Roasters");
        clients.Change(karoo.Id, c => c with { ContactPerson = "Thandi Mokoena", Email = "accounts@karoocoffee.co.za; thandi@karoocoffee.co.za", Phone = "082 555 0100" });
        clients.Add("No details yet");

        var person = Assert.Single(contacts.List());
        Assert.Equal(("Thandi Mokoena", "accounts@karoocoffee.co.za", 2, "082 555 0100", karoo.Id), (person.Name, person.Email, person.Emails.Count, person.Phone, person.ClientId));
        var linked = clients.Get(karoo.Id)!;
        Assert.Equal((person.Id, person.Id), (linked.MainContactId, linked.BillingContactId));

        contacts.Delete(person.Id);
        Assert.Empty(contacts.List()); // not made again
    }

    [Fact]
    public void A_change_to_the_main_contact_shows_on_the_client_and_back()
    {
        var bosveld = clients.Add("Bosveld Builders");
        var (pieter, _) = contacts.Save(Someone("Pieter van Wyk", "pieter@bosveldbuilders.co.za", "082 555 0142", bosveld.Id));
        var (accounts, _) = contacts.Save(Someone("Bosveld accounts", "accounts@bosveldbuilders.co.za", clientId: bosveld.Id));
        contacts.MakeMain(bosveld.Id, pieter!.Id);
        contacts.MakeBilling(bosveld.Id, accounts!.Id);

        var client = clients.Get(bosveld.Id)!;
        Assert.Equal(("Pieter van Wyk", "082 555 0142", "accounts@bosveldbuilders.co.za"), (client.ContactPerson, client.Phone, client.Email));

        contacts.Save(pieter with { Phones = [new ContactWay("Mobile", "083 111 2222")] });
        Assert.Equal("083 111 2222", clients.Get(bosveld.Id)!.Phone);

        clients.SaveDetails(clients.Get(bosveld.Id)! with { ContactPerson = "Pieter J van Wyk", Email = "invoices@bosveldbuilders.co.za" });
        Assert.Equal("Pieter J van Wyk", contacts.Get(pieter.Id)!.Name);
        Assert.Equal("invoices@bosveldbuilders.co.za", contacts.Get(accounts.Id)!.Email);
    }

    [Fact]
    public void The_billing_address_goes_first_and_other_addresses_stay_copied_in()
    {
        var client = new Client { Id = "c1", Name = "Karoo", Email = "thandi@karoo.co.za; accounts@karoo.co.za" };
        var billing = Someone("Accounts", "accounts@karoo.co.za");
        Assert.Equal("accounts@karoo.co.za; thandi@karoo.co.za", ContactRules.Mirror(client, null, billing).Email);
    }

    [Fact]
    public void A_person_leaving_a_client_stops_being_its_contact_person()
    {
        var karoo = clients.Add("Karoo Coffee Roasters");
        var (thandi, _) = contacts.Save(Someone("Thandi Mokoena", "thandi@karoocoffee.co.za", clientId: karoo.Id));
        contacts.MakeMain(karoo.Id, thandi!.Id);
        contacts.MakeBilling(karoo.Id, thandi.Id);

        contacts.Link(thandi.Id, null);
        var client = clients.Get(karoo.Id)!;
        Assert.Equal(((string?)null, (string?)null), (client.MainContactId, client.BillingContactId));
        Assert.Equal("Thandi Mokoena", client.ContactPerson); // what was shown stays, as typed details

        var (again, _) = contacts.Save(contacts.Get(thandi.Id)! with { ClientId = karoo.Id });
        contacts.MakeMain(karoo.Id, again!.Id);
        contacts.Delete(again.Id);
        Assert.Null(clients.Get(karoo.Id)!.MainContactId);
    }

    [Fact]
    public void Groups_are_listed_once_each_A_to_Z()
    {
        var people = new[]
        {
            Someone("A") with { Groups = ["Newsletter", "Hosting clients"] },
            Someone("B") with { Groups = ["hosting clients"] },
        };
        Assert.Equal(["Hosting clients", "Newsletter"], ContactRules.AllGroups(people));
    }

    [Fact]
    public void Saving_a_client_page_opened_before_a_change_in_Contacts_keeps_that_change()
    {
        var bosveld = clients.Add("Bosveld Builders");
        var (pieter, _) = contacts.Save(Someone("Pieter van Wyk", "pieter@bosveldbuilders.co.za", "082 555 0142", bosveld.Id));
        var shown = contacts.MakeMain(bosveld.Id, pieter!.Id)!; // the client page opens with this

        contacts.Save(pieter with { Phones = [new ContactWay("Mobile", "083 444 5555")] }); // changed in Contacts meanwhile

        var (saved, _) = clients.SaveDetails(shown with { Notes = "Prefers WhatsApp" }, loaded: shown); // only the notes were typed
        Assert.Equal(("083 444 5555", "Prefers WhatsApp"), (saved!.Phone, saved.Notes));
        Assert.Equal("083 444 5555", contacts.Get(pieter.Id)!.Phone);
    }

    private static Webignite.Core.Mail.MailSummary Sent(string to, string? name = null) => new()
    {
        AccountId = "a", Folder = "Sent", Uid = 1, Subject = "Hi", Date = DateTimeOffset.UnixEpoch.AddYears(56),
        From = new Webignite.Core.Mail.MailPerson("Eddie", "eddie@webignite.co.za"), To = [new Webignite.Core.Mail.MailPerson(name, to)],
    };

    [Fact]
    public void People_in_Contacts_come_first_in_To_with_the_name_typed_there()
    {
        var thandi = Someone("Thandi Mokoena", "thandi@karoocoffee.co.za");
        var book = Webignite.Core.Mail.AddressBook.Build([], [Sent("thandi@karoocoffee.co.za", "T Mokoena"), Sent("someone@else.co.za", "Someone")],
            ["eddie@webignite.co.za"], DateTimeOffset.UnixEpoch.AddYears(56), [thandi]);
        var first = book.First(c => !c.IsGroup);
        Assert.Equal(("Thandi Mokoena", "thandi@karoocoffee.co.za"), (first.Name, first.Address));
    }

    [Fact]
    public void A_group_puts_everyone_in_it_into_the_box_once()
    {
        var people = new[]
        {
            Someone("Thandi Mokoena", "thandi@karoocoffee.co.za") with { Groups = ["Hosting clients"] },
            Someone("Pieter van Wyk", "pieter@bosveldbuilders.co.za") with { Groups = ["Hosting clients"] },
            Someone("No address") with { Groups = ["Hosting clients"] },
        };
        var book = Webignite.Core.Mail.AddressBook.Build([], [], [], DateTimeOffset.UnixEpoch, people);
        var group = Assert.Single(Webignite.Core.Mail.AddressBook.Match(book, "host"));
        Assert.Equal("Hosting clients (group of 2)", group.Display);
        var text = Webignite.Core.Mail.AddressBook.Insert("Pieter van Wyk <pieter@bosveldbuilders.co.za>, host", group);
        Assert.Equal(2, Webignite.Core.Mail.Recipients.Parse(text).People.Count);
        Assert.Contains("thandi@karoocoffee.co.za", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sender_becomes_a_person_with_their_client()
    {
        var client = new Client { Id = "c1", Name = "Karoo Coffee Roasters" };
        var named = ContactRules.FromSender("\"Thandi Mokoena\"", "thandi@karoocoffee.co.za", client, "p1", DateTimeOffset.UnixEpoch);
        Assert.Equal(("Thandi Mokoena", "c1", "thandi@karoocoffee.co.za"), (named.Name, named.ClientId, named.Email));
        Assert.Equal("accounts", ContactRules.FromSender(null, "accounts@karoo.co.za", null, "p2", DateTimeOffset.UnixEpoch).Name);
    }

    [Fact]
    public void People_mailed_often_but_not_in_Contacts_are_suggested()
    {
        var book = new Webignite.Core.Mail.Contact[]
        {
            new("Lerato Dlamini", "lerato@fynbosphysio.co.za", 40),
            new("Thandi Mokoena", "thandi@karoocoffee.co.za", 240),
            new(null, "noname@x.co.za", 40),
            new("Wrote in once", "once@x.co.za", 2),
        };
        var suggested = ContactRules.MailedOften(book, [Someone("Thandi", "thandi@karoocoffee.co.za")]);
        Assert.Equal(["lerato@fynbosphysio.co.za"], suggested.Select(c => c.Address));
    }

    [Fact]
    public void A_mail_involves_a_person_by_any_of_their_addresses()
    {
        var thandi = Someone("Thandi", "thandi@karoocoffee.co.za") with { Emails = [new ContactWay("Work", "thandi@karoocoffee.co.za"), new ContactWay("Personal", "thandi@gmail.com")] };
        Assert.True(ContactRules.InvolvedIn(thandi, Sent("THANDI@gmail.com")));
        Assert.False(ContactRules.InvolvedIn(thandi, Sent("pieter@bosveld.co.za")));
    }
}
