using Webignite.Core.Clients;

namespace Webignite.Tests;

public class ClientFactsTests
{
    [Fact]
    public void Leaves_out_what_is_empty()
    {
        var facts = ClientFacts.Of(new Client { Id = "1", Name = "Bosveld Builders", Phone = "082 555 0142", Email = " ", HourlyRate = 650m });
        Assert.Equal(["Phone", "Hourly rate"], facts.Select(f => f.Label));
        Assert.Equal(Webignite.Core.Documents.Money.Rand(650m), facts[1].Value);
    }

    [Fact]
    public void Shows_the_billed_name_only_when_the_company_differs()
    {
        Assert.Equal("On quotes and invoices", ClientFacts.Of(new Client { Id = "1", Name = "Jan Botha", Company = "G3" })[0].Label);
        Assert.Empty(ClientFacts.Of(new Client { Id = "1", Name = "G3", Company = "G3" }));
    }
}
