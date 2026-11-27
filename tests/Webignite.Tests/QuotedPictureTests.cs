using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Tests;

public sealed class QuotedPictureTests
{
    [Fact]
    public void Pictures_in_a_quoted_mail_go_as_linked_pictures_not_data_addresses()
    {
        var png = Convert.ToBase64String([0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3, 4]);
        var account = new MailAccount { Id = "a", Address = "eddie@webignite.co.za", DisplayName = "Eddie", ImapHost = "i", SmtpHost = "s" };
        var mail = new OutgoingMail
        {
            AccountId = "a", To = [new MailPerson(null, "ronald@nationalagency.co.za")], Subject = "Fwd: Homepage", Body = "See below.",
            Quote = new Quoted("From Ronald", "Logo", $"<p>Logo</p><img src=\"data:image/png;base64,{png}\"><img src=\"data:image/png;base64,{png}\">", IsForward: true),
        };

        var message = MailComposer.Build(mail, account);

        Assert.DoesNotContain("data:image/", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("cid:", message.HtmlBody, StringComparison.Ordinal);
        Assert.Single(message.BodyParts.OfType<MimeKit.MimePart>(), p => p.ContentType.MediaType == "image");
    }
}
