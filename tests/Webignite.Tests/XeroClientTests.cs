using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Webignite.Core.Clients;
using Webignite.Data;
using Webignite.Services;
using Webignite.Xero;

namespace Webignite.Tests;

/// <summary>Xero's customers as clients: new ones come in, blanks are filled, nothing typed here is changed.</summary>
public sealed class XeroClientTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.FromHours(2));

    private readonly string folder = Directory.CreateTempSubdirectory("webignite-xeroclients-").FullName;
    private readonly LocalStore store;

    public XeroClientTests() => store = new LocalStore(Path.Combine(folder, "webignite.db"), new Vault(RandomNumberGenerator.GetBytes(32)));

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private static Client Client(string id, string name, string? email = null, string? xero = null) =>
        new() { Id = id, Name = name, Email = email, XeroContactId = xero };

    [Fact]
    public void A_new_contact_becomes_a_client_but_suppliers_and_archived_ones_do_not()
    {
        var plan = XeroClients.Plan([], [
            new XeroCustomer("x1", "Karoo Coffee Roasters", "thandi@karoocoffee.co.za"),
            new XeroCustomer("x2", "Afrihost", IsCustomer: false, IsSupplier: true),
            new XeroCustomer("x3", "Old Shop", Active: false),
            new XeroCustomer("x4", "  "),
            new XeroCustomer("x5", "Fynbos Physio", IsCustomer: false), // never invoiced in Xero yet
            new XeroCustomer("x6", "Bosveld Builders", IsCustomer: true, IsSupplier: true), // both
        ]);
        Assert.Equal(["x1", "x5", "x6"], plan.ToAdd.Select(c => c.Id));
        Assert.Empty(plan.ToFill);
    }

    [Fact]
    public void A_client_here_is_matched_by_name_and_only_its_blanks_are_filled()
    {
        var mine = Client("c1", "Bosveld Builders", email: "pieter@bosveldbuilders.co.za") with { Phone = "" };
        var contact = new XeroCustomer("x1", "BOSVELD  builders", "accounts@bosveld.co.za", "Pieter van Wyk", "082 555 0142", "12 Main Rd, Polokwane", "bosveldbuilders.co.za");
        var plan = XeroClients.Plan([mine], [contact]);
        Assert.Empty(plan.ToAdd);
        var (id, matched) = Assert.Single(plan.ToFill);
        Assert.Equal("c1", id);
        var filled = XeroClients.Fill(mine, matched);
        Assert.Equal("pieter@bosveldbuilders.co.za", filled.Email);
        Assert.Equal("Pieter van Wyk", filled.ContactPerson);
        Assert.Equal("082 555 0142", filled.Phone);
        Assert.Equal("12 Main Rd, Polokwane", filled.Address);
        Assert.Equal("x1", filled.XeroContactId);
        Assert.Equal("Bosveld Builders", filled.Name);
    }

    [Fact]
    public void A_client_linked_to_a_contact_is_found_after_a_rename_in_xero()
    {
        var plan = XeroClients.Plan([Client("c1", "Fynbos Physio", xero: "x9")], [new XeroCustomer("x9", "Fynbos Physiotherapy", "admin@fynbos.co.za")]);
        Assert.Empty(plan.ToAdd);
        Assert.Equal("c1", Assert.Single(plan.ToFill).ClientId);
    }

    [Fact]
    public void Nothing_to_do_when_everything_is_known()
    {
        var known = Client("c1", "Karoo Coffee Roasters", email: "thandi@karoocoffee.co.za", xero: "x1");
        Assert.True(XeroClients.Plan([known], [new XeroCustomer("x1", "Karoo Coffee Roasters", "other@karoo.co.za")]).IsEmpty);
    }

    [Fact]
    public void Two_contacts_with_one_name_make_one_client()
    {
        var plan = XeroClients.Plan([], [new XeroCustomer("x1", "Karoo Coffee"), new XeroCustomer("x2", "karoo coffee")]);
        Assert.Single(plan.ToAdd);
    }

    [Fact]
    public void The_summary_says_what_changed()
    {
        Assert.Equal("2 new clients from Xero, 1 client filled in from Xero", XeroClients.Summary(2, 1));
        Assert.Equal("", XeroClients.Summary(0, 0));
    }

    [Fact]
    public async Task The_watch_adds_and_fills_without_undoing_what_was_typed()
    {
        store.SaveClient(Client("c1", "Bosveld Builders") with { Notes = "Prefers WhatsApp" });
        var watch = new XeroClientWatch(store, _ => Task.FromResult<IReadOnlyList<XeroCustomer>>([
            new XeroCustomer("x1", "Bosveld Builders", "pieter@bosveldbuilders.co.za"),
            new XeroCustomer("x2", "Karoo Coffee Roasters", "thandi@karoocoffee.co.za", Phone: "021 555 0100"),
        ]), new FixedClock(Now));

        Assert.True(watch.Due(Now));
        var news = await watch.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal((1, 1, null), (news.Added, news.Filled, news.Problem));
        var clients = store.ListClients();
        var bosveld = clients.Single(c => c.Name == "Bosveld Builders");
        Assert.Equal("Prefers WhatsApp", bosveld.Notes);
        Assert.Equal("pieter@bosveldbuilders.co.za", bosveld.Email);
        Assert.Equal("021 555 0100", clients.Single(c => c.Name == "Karoo Coffee Roasters").Phone);
        Assert.False(watch.Due(Now.AddHours(3)));
        Assert.True(watch.Due(Now.AddHours(4)));

        var again = await watch.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal((0, 0), (again.Added, again.Filled));
    }

    [Fact]
    public async Task Without_xero_it_says_so_and_changes_nothing()
    {
        var watch = new XeroClientWatch(store, _ => null, new FixedClock(Now));
        var news = await watch.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Connect Xero in Settings first.", news.Problem);
        Assert.Empty(store.ListClients());
    }

    [Fact]
    public void A_xero_contact_reads_with_its_person_phone_and_address()
    {
        var json = JsonNode.Parse("""
            {
              "ContactID": "x1", "Name": "Karoo Coffee Roasters", "ContactStatus": "ACTIVE", "IsCustomer": true,
              "FirstName": "Thandi", "LastName": "Mokoena", "EmailAddress": "thandi@karoocoffee.co.za", "Website": "karoocoffee.co.za",
              "Phones": [
                { "PhoneType": "DEFAULT", "PhoneNumber": "555 0100", "PhoneAreaCode": "021", "PhoneCountryCode": "27" },
                { "PhoneType": "MOBILE", "PhoneNumber": "", "PhoneAreaCode": "" }
              ],
              "Addresses": [
                { "AddressType": "POBOX", "AddressLine1": "PO Box 12" },
                { "AddressType": "STREET", "AddressLine1": "4 Long St", "City": "Cape Town", "PostalCode": "8001" }
              ]
            }
            """)!.AsObject();
        var contact = XeroApi.ToCustomer(json);
        Assert.Equal(new XeroCustomer("x1", "Karoo Coffee Roasters", "thandi@karoocoffee.co.za", "Thandi Mokoena", "021 555 0100", "4 Long St, Cape Town, 8001", "karoocoffee.co.za"), contact);

        var abroad = XeroApi.ToCustomer(JsonNode.Parse("""{ "ContactID": "x2", "Name": "Acme", "ContactStatus": "ARCHIVED", "Phones": [{ "PhoneType": "MOBILE", "PhoneNumber": "7700 900123", "PhoneCountryCode": "+44" }] }""")!.AsObject());
        Assert.Equal("+44 7700 900123", abroad.Phone);
        Assert.False(abroad.Active);
        Assert.False(abroad.IsCustomer);
        Assert.False(abroad.IsSupplier);
        Assert.True(XeroApi.ToCustomer(JsonNode.Parse("""{ "ContactID": "x3", "Name": "Afrihost", "IsSupplier": true }""")!.AsObject()).OnlySupplier);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
