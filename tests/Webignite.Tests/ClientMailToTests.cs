using Webignite.Core.Clients;

namespace Webignite.Tests;

public class ClientMailToTests
{
    private static readonly Client G3 = new() { Id = "c", Name = "Francois Booysens", Company = "G3", MainContactId = "owner", BillingContactId = "accounts" };

    private static Person? Who(string id) => new() { Id = id, Name = id, Emails = [new ContactWay("Work", id + "@g3.co.za")] };

    [Fact]
    public void Quotes_and_statements_follow_the_invoices_until_given_their_own_person()
    {
        Assert.Equal("accounts@g3.co.za", ClientMailTo.Address(G3, MailFor.Quotes, Who));
        Assert.Equal("accounts@g3.co.za", ClientMailTo.Address(G3, MailFor.Statements, Who));

        var changed = ClientMailTo.Send(G3, MailFor.Quotes, "owner");

        Assert.Equal("owner@g3.co.za", ClientMailTo.Address(changed, MailFor.Quotes, Who));
        Assert.Equal("accounts@g3.co.za", ClientMailTo.Address(changed, MailFor.Invoices, Who));
    }

    [Fact]
    public void Project_mail_goes_to_the_contact_person_unless_someone_else_is_picked()
    {
        Assert.Equal("owner@g3.co.za", ClientMailTo.Address(G3, MailFor.Projects, Who));
        Assert.Equal("designer@g3.co.za", ClientMailTo.Address(ClientMailTo.Send(G3, MailFor.Projects, "designer"), MailFor.Projects, Who));
    }

    [Fact]
    public void Moving_the_invoices_leaves_quotes_and_statements_where_they_were_going()
    {
        var changed = ClientMailTo.Send(G3, MailFor.Invoices, "bookkeeper");

        Assert.Equal("bookkeeper", ClientMailTo.ContactId(changed, MailFor.Invoices));
        Assert.Equal("accounts", ClientMailTo.ContactId(changed, MailFor.Quotes));
        Assert.Equal("accounts", ClientMailTo.ContactId(changed, MailFor.Statements));
    }

    [Fact]
    public void Nobody_picked_or_no_address_gives_nothing()
    {
        Assert.Null(ClientMailTo.Address(G3 with { BillingContactId = null }, MailFor.Invoices, Who));
        Assert.Null(ClientMailTo.Address(G3, MailFor.Invoices, id => new Person { Id = id, Name = id }));
    }

    [Fact]
    public void Someone_who_leaves_is_no_longer_pointed_at()
    {
        var changed = ClientMailTo.Without(ClientMailTo.Send(G3, MailFor.Statements, "accounts"), "accounts");

        Assert.False(ClientMailTo.PointsAt(changed, "accounts"));
        Assert.Equal("owner", changed.MainContactId);
    }

    [Fact]
    public void Says_what_a_person_gets()
    {
        Assert.Equal("quotes, invoices, statements and reminders", ClientMailTo.Gets(G3, "accounts"));
        Assert.Equal("project mail", ClientMailTo.Gets(G3, "owner"));
    }
}
