using Webignite.Core.Projects;

namespace Webignite.Tests;

/// <summary>Names for the two of you: from the sign-in emails until typed, and as each person names themselves.</summary>
public sealed class PeopleTests
{
    private static readonly TeamSettings MadeUp = new()
    {
        Me = new Teammate { Id = "a", Name = PeopleRules.DefaultMe },
        Partner = new Teammate { Id = "b", Name = ProjectRules.DefaultPartner },
    };

    [Theory]
    [InlineData("eddie@webignite.co.za", "Eddie")]
    [InlineData("jaco.van.wyk@example.co.za", "Jaco Van Wyk")]
    [InlineData("LIZE_SMIT+work@example.com", "Lize Smit")]
    [InlineData("info2@example.com", "Info2")]
    [InlineData("", "")]
    public void A_name_comes_from_an_email(string email, string name) => Assert.Equal(name, PeopleRules.NameFromEmail(email));

    [Fact]
    public void Made_up_names_take_the_workspace_emails_and_typed_ones_stay()
    {
        var named = PeopleRules.Named(MadeUp, "eddie@webignite.co.za", ["eddie@webignite.co.za", "jaco@webignite.co.za"]);
        Assert.Equal("Eddie", named.Me!.Name);
        Assert.Equal("Jaco", named.Partner!.Name);
        Assert.Equal("jaco@webignite.co.za", named.Partner.Email);
        Assert.Equal("a", named.Me.Id); // ids never change

        var typed = MadeUp with { Me = MadeUp.Me! with { Name = "Eddie T" }, Partner = MadeUp.Partner! with { Name = "JJ" } };
        var kept = PeopleRules.Named(typed, "eddie@webignite.co.za", ["eddie@webignite.co.za", "jaco@webignite.co.za"]);
        Assert.Equal("Eddie T", kept.Me!.Name);
        Assert.Equal("JJ", kept.Partner!.Name);

        // Alone in the workspace: nothing for the partner yet.
        Assert.Equal(ProjectRules.DefaultPartner, PeopleRules.Named(MadeUp, "eddie@webignite.co.za", ["eddie@webignite.co.za"]).Partner!.Name);
        Assert.Same(kept, PeopleRules.Named(kept, "eddie@webignite.co.za", ["eddie@webignite.co.za", "jaco@webignite.co.za"]));
    }

    [Fact]
    public void My_partner_s_own_name_wins_and_made_up_names_from_the_other_pc_are_ignored()
    {
        var team = MadeUp with { Partner = MadeUp.Partner! with { Name = "JJ" } };
        Assert.Equal("Jaco", PeopleRules.TakeIn(team, new Teammate { Id = "b", Name = "Jaco" }).Partner!.Name);
        Assert.Same(team, PeopleRules.TakeIn(team, new Teammate { Id = "b", Name = PeopleRules.DefaultMe }));
        Assert.Same(team, PeopleRules.TakeIn(team, new Teammate { Id = "someone else", Name = "Pieter" }));
        // My own name from my other PC, only while this one has the made-up one.
        Assert.Equal("Eddie", PeopleRules.TakeIn(team, new Teammate { Id = "a", Name = "Eddie" }).Me!.Name);
        var mine = team with { Me = team.Me! with { Name = "Eddie T" } };
        Assert.Same(mine, PeopleRules.TakeIn(mine, new Teammate { Id = "a", Name = "Eddie" }));
    }
}
