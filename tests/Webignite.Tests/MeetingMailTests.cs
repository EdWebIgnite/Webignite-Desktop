using MimeKit;
using Webignite.Core.Calendar;
using Webignite.Mail;

namespace Webignite.Tests;

public class MeetingMailTests
{
    private const string Invite = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Site review\r\nDTSTART:20261120T090000\r\nDTEND:20261120T093000\r\nURL:https://meet.google.com/abc-defg-hij\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static byte[] Bytes(MimeEntity body)
    {
        var message = new MimeMessage { Subject = "Site review", Body = body };
        message.From.Add(new MailboxAddress("Pieter", "pieter@bosveldbuilders.co.za"));
        using var raw = new MemoryStream();
        message.WriteTo(raw);
        return raw.ToArray();
    }

    [Fact]
    public void An_invite_sent_as_another_form_of_the_body_is_read_as_Outlook_and_Teams_send_it()
    {
        var mail = MailReader.Read(Bytes(new MultipartAlternative
        {
            new TextPart("plain") { Text = "Please join us." },
            new TextPart("calendar") { Text = Invite },
        }), false);

        var meeting = MeetingInvites.InMail("Site review", mail.Text, mail.Html, mail.Calendar, new DateOnly(2026, 11, 12), TimeZoneInfo.Utc)!;
        Assert.Equal(new DateOnly(2026, 11, 20), meeting.Date);
        Assert.Equal(new TimeOnly(9, 0), meeting.Start);
        Assert.Equal(new TimeOnly(9, 30), meeting.End);
        Assert.Equal("https://meet.google.com/abc-defg-hij", meeting.JoinLink);
    }

    [Fact]
    public void An_invite_attached_as_an_ics_file_is_read_too()
    {
        var builder = new BodyBuilder { TextBody = "See attached." };
        builder.Attachments.Add("invite.ics", System.Text.Encoding.UTF8.GetBytes(Invite), new ContentType("application", "octet-stream"));

        var mail = MailReader.Read(Bytes(builder.ToMessageBody()), false);

        Assert.Contains("SUMMARY:Site review", mail.Calendar);
    }

    [Fact]
    public void Ordinary_mail_has_no_invite()
    {
        var mail = MailReader.Read(Bytes(new TextPart("plain") { Text = "Thanks, the quote looks good." }), false);
        Assert.Null(mail.Calendar);
    }
}
