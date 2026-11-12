using Webignite.Core.Web;

namespace Webignite.Tests;

public class DomainAvailabilityTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Fact]
    public void A_bare_name_is_tried_with_the_usual_endings_co_za_first()
    {
        var names = DomainAvailability.Names("  Karoo Coffee ");
        Assert.Equal("karoocoffee.co.za", names[0]);
        Assert.Equal("karoocoffee.com", names[1]);
        Assert.Equal(DomainAvailability.Endings.Count, names.Count);
    }

    [Theory]
    [InlineData("https://www.karoocoffee.com/menu", "karoocoffee.com", "karoocoffee.co.za")]
    [InlineData("karoo-coffee.org.za", "karoo-coffee.org.za", "karoo-coffee.co.za")]
    [InlineData("karoocoffee.io", "karoocoffee.io", "karoocoffee.co.za")]
    public void A_name_typed_with_an_ending_has_that_one_first(string typed, string first, string second)
    {
        var names = DomainAvailability.Names(typed);
        Assert.Equal(first, names[0]);
        Assert.Equal(second, names[1]);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("!!!")]
    public void Nothing_usable_gives_nothing_to_check(string typed) => Assert.Empty(DomainAvailability.Names(typed));

    [Fact]
    public void Dns_that_has_never_heard_of_a_name_makes_it_look_free()
    {
        var (status, has) = DomainAvailability.Dns("""{"Status":3,"Question":[{"name":"x.co.za.","type":2}],"Authority":[{"name":"co.za.","type":6,"data":"ns.coza.net.za."}]}""");
        Assert.Equal(3, status);
        Assert.False(has);
        var check = DomainAvailability.Judge(new DomainFacts { Domain = "x.co.za", Today = Today, DnsStatus = status, HasNameServers = has });
        Assert.Equal(DomainState.LooksFree, check.State);
        Assert.True(check.CanBeHad);
    }

    [Fact]
    public void A_name_with_name_servers_is_taken_even_when_its_registry_says_nothing()
    {
        var (status, has) = DomainAvailability.Dns("""{"Status":0,"Answer":[{"name":"webignite.co.za.","type":2,"TTL":7200,"data":"ns1.host-h.net."}]}""");
        var check = DomainAvailability.Judge(new DomainFacts { Domain = "webignite.co.za", Today = Today, DnsStatus = status, HasNameServers = has });
        Assert.Equal(DomainState.Taken, check.State);
        Assert.Equal("Taken", check.Verdict);
    }

    [Fact]
    public void A_registry_that_answers_and_has_no_such_name_makes_it_free() =>
        Assert.Equal(DomainState.Free, DomainAvailability.Judge(new DomainFacts { Domain = "x.com", Today = Today, DnsStatus = 3, RegistrySaysNone = true }).State);

    [Fact]
    public void A_registrys_record_makes_it_taken_and_says_until_when()
    {
        const string record = """{"events":[{"eventAction":"expiration","eventDate":"2027-03-14T00:00:00Z"}],"entities":[{"roles":["registrar"],"vcardArray":["vcard",[["fn",{},"text","GoDaddy.com, LLC"]]]}]}""";
        var check = DomainAvailability.Judge(new DomainFacts { Domain = "karoocoffee.com", Today = Today, DnsStatus = 3, RegistryJson = record });
        Assert.Equal(DomainState.Taken, check.State);
        Assert.Contains("GoDaddy.com, LLC", check.Detail);
        Assert.Contains("2027", check.Detail);
    }

    [Fact]
    public void With_no_answer_from_anyone_it_says_it_is_not_sure()
    {
        var check = DomainAvailability.Judge(new DomainFacts { Domain = "x.co.za", Today = Today });
        Assert.Equal(DomainState.Unsure, check.State);
        Assert.False(check.CanBeHad);
        Assert.Equal((null, false), DomainAvailability.Dns("not json"));
    }
}
