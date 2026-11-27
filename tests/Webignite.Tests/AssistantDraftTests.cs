using Webignite.Core.Mail;

namespace Webignite.Tests;

public sealed class AssistantDraftTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 27, 14, 12, 0, TimeSpan.FromHours(2));

    private static MailSummary Draft(uint uid, string subject, string? inReplyTo, string to, int minutes) => new()
    {
        AccountId = "a", Folder = "Drafts", Uid = uid, Subject = subject, InReplyTo = inReplyTo,
        To = [new MailPerson(null, to)], Date = At.AddMinutes(minutes),
    };

    [Fact]
    public void A_second_draft_of_the_same_reply_stands_in_for_the_earlier_ones_newest_first()
    {
        var drafts = new[]
        {
            Draft(1, "RE: NPA WEBSITE", "<q@npa>", "ronald@nationalagency.co.za", 0),
            Draft(2, "RE: NPA WEBSITE", "<q@npa>", "ronald@nationalagency.co.za", 1),
            Draft(3, "RE: Something else", "<other@npa>", "ronald@nationalagency.co.za", 2),
        };
        var reply = new OutgoingMail { AccountId = "a", Subject = "RE: NPA WEBSITE", InReplyTo = "<q@npa>" };

        Assert.Equal([2u, 1u], AssistantMail.SameDraft(drafts, reply).Select(d => d.Uid));
    }

    [Fact]
    public void A_new_mail_matches_only_the_same_subject_to_the_same_people()
    {
        var drafts = new[]
        {
            Draft(1, "Proofs  for Friday", null, "PIETER@bosveldbuilders.co.za", 0),
            Draft(2, "Proofs for Friday", null, "someone@else.co.za", 1),
            Draft(3, "Proofs for Friday", "<x@y>", "pieter@bosveldbuilders.co.za", 2),
        };
        var fresh = new OutgoingMail { AccountId = "a", Subject = "proofs for friday", To = [new MailPerson("Pieter", "pieter@bosveldbuilders.co.za")] };

        Assert.Equal([1u], AssistantMail.SameDraft(drafts, fresh).Select(d => d.Uid));
        Assert.Empty(AssistantMail.SameDraft(drafts, fresh with { Subject = "" }));
    }
}
