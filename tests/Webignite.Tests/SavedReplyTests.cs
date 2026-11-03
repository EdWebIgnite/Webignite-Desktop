using Webignite.Core.Clients;
using Webignite.Core.Mail;

namespace Webignite.Tests;

/// <summary>Saved replies, with the client's name filled in.</summary>
public sealed class SavedReplyTests
{
    private static readonly Client Karoo = new()
    {
        Id = "c1", Name = "Karoo Coffee Roasters", ContactPerson = "Thandi Mokoena", Email = "thandi@karoocoffee.co.za", Website = "https://karoocoffee.co.za",
    };

    private static readonly Client Fynbos = new() { Id = "c2", Name = "Fynbos Physio", Email = "lerato@gmail.com" };

    [Fact]
    public void The_client_is_found_by_address_then_by_domain_but_never_by_a_free_mail_domain()
    {
        Assert.Equal(Karoo, SavedReplies.ClientFor([Fynbos, Karoo], "THANDI@karoocoffee.co.za"));
        Assert.Equal(Karoo, SavedReplies.ClientFor([Fynbos, Karoo], "accounts@karoocoffee.co.za"));
        Assert.Equal(Fynbos, SavedReplies.ClientFor([Fynbos, Karoo], "lerato@gmail.com"));
        Assert.Null(SavedReplies.ClientFor([Fynbos, Karoo], "someone@gmail.com"));
        Assert.Null(SavedReplies.ClientFor([Fynbos, Karoo], null));
    }

    [Fact]
    public void The_first_name_comes_from_the_contact_person_or_the_name_on_the_address()
    {
        Assert.Equal("Thandi", SavedReplies.FirstName(new MailPerson(null, "thandi@karoocoffee.co.za"), Karoo));
        Assert.Equal("Sipho", SavedReplies.FirstName(new MailPerson("Sipho Ndlovu", "accounts@karoocoffee.co.za"), Karoo));
        Assert.Equal("Lerato", SavedReplies.FirstName(new MailPerson("Lerato Dlamini", "lerato@x.co.za"), null));
        Assert.Null(SavedReplies.FirstName(new MailPerson(null, "info@x.co.za"), null));
        Assert.Null(SavedReplies.FirstName(null, null));
    }

    [Fact]
    public void Name_and_client_are_filled_in()
    {
        Assert.Equal("Hi Thandi, thanks for the payment from Karoo Coffee Roasters.",
            SavedReplies.Fill("Hi {name}, thanks for the payment from {client}.", "Thandi", "Karoo Coffee Roasters"));
    }

    [Fact]
    public void An_unknown_name_is_left_out_cleanly()
    {
        Assert.Equal("Hi, thanks for the payment.", SavedReplies.Fill("Hi {name}, thanks for the payment.", null, null));
        Assert.Equal("Thanks for the payment.", SavedReplies.Fill("{name}, Thanks for the payment.", null, null));
        Assert.Equal("Thanks for the payment.", SavedReplies.Fill("Thanks for the payment{client}.", null, " "));
    }

    [Fact]
    public void Text_goes_in_at_the_cursor()
    {
        Assert.Equal(("Hi Thandi,\n\nPaid, thanks.\n\nEddie", 25), SavedReplies.Insert("Hi Thandi,\n\n\n\nEddie", 12, "Paid, thanks."));
        Assert.Equal(("abcX", 4), SavedReplies.Insert("abc", 99, "X"));
    }

    [Fact]
    public void A_reply_needs_a_new_name_and_some_text()
    {
        SavedReply[] existing = [new("r1", "Payment received", "Thanks")];
        Assert.Equal(2, SavedReplies.Check(" ", " ", existing).Count);
        Assert.Single(SavedReplies.Check("payment received", "Thanks again", existing));
        Assert.Empty(SavedReplies.Check("Quote follow-up", "Any news on the quote?", existing));
    }
}
