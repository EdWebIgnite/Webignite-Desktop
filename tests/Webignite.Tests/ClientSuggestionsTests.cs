using Webignite.Core.Clients;

namespace Webignite.Tests;

public class ClientSuggestionsTests
{
    private static readonly Client Bosveld = new() { Id = "b", Name = "Bosveld Builders", Phone = "082 555 0142", Email = "pieter@bosveld.co.za" };

    [Fact]
    public void Only_details_that_differ_are_changes()
    {
        var suggestion = new ClientSuggestion { Who = "Claude", Phone = "082 555 0142", Email = "accounts@bosveld.co.za", Website = "" };
        var changes = ClientSuggestions.Changes(Bosveld, suggestion);
        Assert.Equal([ClientSuggestions.Email], changes.Select(c => c.Field));
        Assert.Equal(("pieter@bosveld.co.za", "accounts@bosveld.co.za"), (changes[0].Before, changes[0].After));
    }

    [Fact]
    public void Accepting_some_changes_leaves_the_rest_waiting()
    {
        var suggestion = new ClientSuggestion { Who = "ChatGPT", Email = "accounts@bosveld.co.za", Address = "12 Main Rd, Brits" };
        var applied = ClientSuggestions.Apply(Bosveld, suggestion, [ClientSuggestions.Address]);
        Assert.Equal("12 Main Rd, Brits", applied.Address);
        Assert.Equal("pieter@bosveld.co.za", applied.Email);

        var left = ClientSuggestions.Without(applied, suggestion, [ClientSuggestions.Address]);
        Assert.Equal([ClientSuggestions.Email], ClientSuggestions.Changes(applied, left!).Select(c => c.Field));
        Assert.Null(ClientSuggestions.Without(applied, left!, [ClientSuggestions.Email]));
    }

    [Fact]
    public void An_empty_value_suggests_clearing_and_a_newer_suggestion_wins()
    {
        var older = new ClientSuggestion { Who = "Claude", Phone = "011 000 0000", Email = "old@bosveld.co.za" };
        var newer = new ClientSuggestion { Who = "Claude", Phone = "" };
        var merged = ClientSuggestions.Merge(older, newer);
        Assert.Equal(("", "old@bosveld.co.za"), (merged.Phone, merged.Email));
        Assert.Null(ClientSuggestions.Apply(Bosveld, merged).Phone);
    }

    [Fact]
    public void The_heading_says_who_and_that_nothing_changes_yet()
    {
        Assert.Equal("Claude suggests 2 changes. Nothing changes until you accept them.", ClientSuggestions.Heading(new ClientSuggestion { Who = "Claude" }, 2));
        Assert.StartsWith("ChatGPT added this client.", ClientSuggestions.Heading(new ClientSuggestion { Who = "ChatGPT", NewClient = true }, 3), StringComparison.Ordinal);
    }
}
