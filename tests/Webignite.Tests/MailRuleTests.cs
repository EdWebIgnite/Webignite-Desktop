using Webignite.Core.Mail;

namespace Webignite.Tests;

/// <summary>Mail rules: "Mail from fnb.co.za goes to Banking and gets the Accounts colour."</summary>
public sealed class MailRuleTests
{
    private static MailSummary Mail(string from, string subject = "Your statement", uint uid = 1, string account = "work",
        bool seen = false, string to = "eddie@webignite.co.za", string folder = "INBOX") => new()
    {
        AccountId = account, Folder = folder, Uid = uid, Subject = subject, Seen = seen,
        From = new MailPerson(null, from), To = [new MailPerson(null, to)],
    };

    private static MailRule Rule(RuleMatch match, string text, string? moveTo = null, string? category = null,
        bool important = false, bool read = false, string? account = "work") => new()
    {
        Id = MailRule.NewId(), AccountId = account, Match = match, Text = text, MoveTo = moveTo, Category = category,
        MarkImportant = important, MarkRead = read,
    };

    [Theory]
    [InlineData("inContact@fnb.co.za", true)]
    [InlineData("alerts@mail.fnb.co.za", true)]
    [InlineData("someone@notfnb.co.za", false)]
    [InlineData("fnb.co.za@gmail.com", false)]
    public void Domain_rules_match_the_domain_and_its_subdomains(string from, bool expected) =>
        Assert.Equal(expected, MailRules.Matches(Rule(RuleMatch.Domain, "fnb.co.za", category: "accounts"), Mail(from)));

    [Fact]
    public void Sender_subject_and_to_rules_look_for_the_words_in_any_letter_case()
    {
        Assert.True(MailRules.Matches(Rule(RuleMatch.Sender, "ACCOUNTS@", read: true), Mail("accounts@karoocoffee.co.za")));
        Assert.True(MailRules.Matches(Rule(RuleMatch.Subject, "proof of payment", read: true), Mail("x@y.co.za", "Proof of payment INV-0107")));
        Assert.True(MailRules.Matches(Rule(RuleMatch.To, "accounts@webignite", read: true), Mail("x@y.co.za", to: "accounts@webignite.co.za")));
        Assert.False(MailRules.Matches(Rule(RuleMatch.To, "accounts@webignite", read: true), Mail("x@y.co.za")));
    }

    [Fact]
    public void A_rule_for_one_mailbox_leaves_the_others_alone_and_a_rule_switched_off_does_nothing()
    {
        Assert.False(MailRules.Matches(Rule(RuleMatch.Domain, "fnb.co.za", read: true, account: "accounts"), Mail("a@fnb.co.za")));
        Assert.True(MailRules.Matches(Rule(RuleMatch.Domain, "fnb.co.za", read: true, account: null), Mail("a@fnb.co.za")));
        Assert.False(MailRules.Matches(Rule(RuleMatch.Domain, "fnb.co.za", read: true) with { On = false }, Mail("a@fnb.co.za")));
    }

    [Fact]
    public void Matching_rules_add_up_and_the_first_move_decides_the_folder()
    {
        var outcomes = MailRules.Apply(
        [
            Rule(RuleMatch.Domain, "fnb.co.za", moveTo: "INBOX.Banking", category: "accounts"),
            Rule(RuleMatch.Subject, "statement", moveTo: "INBOX.Other", read: true),
            Rule(RuleMatch.Domain, "karoo.co.za", important: true),
        ], [Mail("a@fnb.co.za", uid: 1), Mail("b@xneelo.co.za", "Hosting", uid: 2)]);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(1u, outcome.Mail.Uid);
        Assert.Equal("INBOX.Banking", outcome.MoveTo);
        Assert.Equal(["accounts"], outcome.Categories);
        Assert.True(outcome.MarkRead);
        Assert.False(outcome.MarkImportant);
    }

    [Fact]
    public void Nothing_is_done_twice()
    {
        var done = Mail("a@fnb.co.za", seen: true, folder: "INBOX.Banking") with { Keywords = ["accounts"] };
        Assert.Empty(MailRules.Apply([Rule(RuleMatch.Domain, "fnb.co.za", moveTo: "INBOX.Banking", category: "accounts", read: true)], [done]));
    }

    [Fact]
    public void Only_mail_that_arrived_since_the_last_look_is_picked_and_the_first_look_picks_nothing()
    {
        MailSummary[] inbox = [Mail("a@x.co.za", uid: 10), Mail("b@x.co.za", uid: 12), Mail("c@x.co.za", uid: 15)];
        var (first, mark) = MailRules.Arrived(inbox, null);
        Assert.Empty(first);
        Assert.Equal(15u, mark);

        var (fresh, next) = MailRules.Arrived(inbox, 11);
        Assert.Equal([12u, 15u], fresh.Select(m => m.Uid));
        Assert.Equal(15u, next);
    }

    [Fact]
    public void A_rule_needs_words_something_to_do_and_one_mailbox_to_move()
    {
        Assert.Equal(2, MailRules.Check(Rule(RuleMatch.Domain, " ")).Count);
        Assert.Single(MailRules.Check(Rule(RuleMatch.Domain, "fnb.co.za", moveTo: "INBOX.Banking", account: null)));
        Assert.Empty(MailRules.Check(Rule(RuleMatch.Domain, "fnb.co.za", moveTo: "INBOX.Banking")));
    }

    [Fact]
    public void A_rule_reads_as_a_sentence()
    {
        var text = MailRules.Describe(Rule(RuleMatch.Domain, "@fnb.co.za", moveTo: "INBOX.Banking", category: "accounts", read: true),
            path => path.Split('.')[^1], keyword => keyword == "accounts" ? "Accounts" : keyword);
        Assert.Equal("Mail from fnb.co.za goes to Banking, gets Accounts and is marked read.", text);
    }
}
