using Webignite.Core.Clients;
using Webignite.Core.Mail;

namespace Webignite.Tests;

public sealed class AddressBookTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly string[] Own = ["eddie@webignite.co.za", "accounts@webignite.co.za"];

    private static MailSummary Mail(string from, string? fromName, string[] to, int daysAgo, uint uid) => new()
    {
        AccountId = "m1", Folder = "INBOX", Uid = uid, Date = Now.AddDays(-daysAgo),
        From = new MailPerson(fromName, from), To = to.Select(t => new MailPerson(null, t)).ToList(),
    };

    private static IReadOnlyList<Contact> Book() => AddressBook.Build(
        [new Client { Id = "c", Name = "Karoo Coffee Roasters", ContactPerson = "Thandi Mokoena", Email = "thandi@karoocoffee.co.za; accounts@karoocoffee.co.za" }],
        [
            Mail("eddie@webignite.co.za", "Eddie", ["pieter@bosveldbuilders.co.za"], 3, 1),
            Mail("pieter@bosveldbuilders.co.za", "Pieter van Wyk", ["eddie@webignite.co.za"], 2, 2),
            Mail("noreply@xero.com", "Xero", ["accounts@webignite.co.za"], 1, 3),
            Mail("lerato@fynbosphysio.co.za", "Lerato Dlamini", ["eddie@webignite.co.za"], 1, 4),
        ],
        Own, Now);

    [Fact]
    public void Clients_and_people_written_to_rank_first()
    {
        var book = Book();
        Assert.Equal(["accounts@karoocoffee.co.za", "thandi@karoocoffee.co.za", "pieter@bosveldbuilders.co.za", "lerato@fynbosphysio.co.za"],
            book.Select(c => c.Address));
        Assert.Equal("Pieter van Wyk", book.Single(c => c.Address.StartsWith("pieter", StringComparison.Ordinal)).Name);
        Assert.DoesNotContain(book, c => c.Address.Contains("xero") || c.Address.Contains("webignite"));
    }

    [Fact]
    public void A_mail_I_sent_is_about_the_people_it_went_to_and_never_about_me()
    {
        var sent = Mail("eddie@webignite.co.za", "Eddie", ["derik@example.co.za", "accounts@webignite.co.za", "derik@example.co.za"], 0, 9);
        Assert.Equal(["derik@example.co.za"], AddressBook.Others(sent, Own).Select(p => p.Address));
        var received = Mail("pieter@bosveldbuilders.co.za", "Pieter van Wyk", ["eddie@webignite.co.za"], 0, 10);
        Assert.Equal(["pieter@bosveldbuilders.co.za"], AddressBook.Others(received, Own).Select(p => p.Address));
        // A note to myself is about nobody.
        Assert.Empty(AddressBook.Others(Mail("eddie@webignite.co.za", "Eddie", ["eddie@webignite.co.za"], 0, 11), Own));
    }

    [Fact]
    public void People_written_to_are_remembered_and_suggested_without_the_sent_folder()
    {
        var mailed = AddressBook.Remember([], [new MailPerson("Derik Ungerer", "derik@example.co.za"), new MailPerson(null, "not an address")], Now);
        Assert.Equal([new Mailed("Derik Ungerer", "derik@example.co.za", Now)], mailed);
        // Written to again with only the address: once in the list, the name kept, the newer time.
        mailed = AddressBook.Remember(mailed, [new MailPerson(null, "DERIK@example.co.za")], Now.AddDays(1));
        Assert.Equal("Derik Ungerer", Assert.Single(mailed).Name);
        Assert.Equal(Now.AddDays(1), mailed[0].When);

        var book = AddressBook.Build([], [], Own, Now, mailed: [.. mailed, new Mailed("Eddie", "eddie@webignite.co.za", Now)]);
        Assert.Equal("derik@example.co.za", Assert.Single(book).Address, ignoreCase: true);
        Assert.Equal("Derik Ungerer", Assert.Single(AddressBook.Match(book, "der")).Name);
    }

    [Theory]
    [InlineData("pie", "pieter@bosveldbuilders.co.za")]
    [InlineData("van", "pieter@bosveldbuilders.co.za")]
    [InlineData("fynbos", "lerato@fynbosphysio.co.za")]
    [InlineData("thandi@karoocoffee.co.za, ler", "lerato@fynbosphysio.co.za")]
    public void Typing_finds_by_name_address_or_domain(string typed, string expected) =>
        Assert.Equal(expected, Assert.Single(AddressBook.Match(Book(), typed)).Address);

    [Fact]
    public void People_already_in_the_box_are_not_offered_again()
    {
        var found = AddressBook.Match(Book(), "thandi@karoocoffee.co.za; karoo");
        Assert.Equal("accounts@karoocoffee.co.za", Assert.Single(found).Address);
        Assert.Empty(AddressBook.Match(Book(), "thandi@karoocoffee.co.za, "));
    }

    [Fact]
    public void Choosing_replaces_only_the_part_being_typed()
    {
        Assert.Equal("Pieter van Wyk <pieter@bosveldbuilders.co.za>, ", AddressBook.Complete("pie", "Pieter van Wyk <pieter@bosveldbuilders.co.za>"));
        Assert.Equal("thandi@karoocoffee.co.za, lerato@fynbosphysio.co.za, ", AddressBook.Complete("thandi@karoocoffee.co.za,ler", "lerato@fynbosphysio.co.za"));
    }
}
