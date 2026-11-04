using Webignite.Core.Mail;

namespace Webignite.Tests;

public sealed class MailFormatTests
{
    [Fact]
    public void Plain_text_keeps_its_lines()
    {
        Assert.Equal("<div style=\"white-space: pre-wrap\">Hi Thandi,<br><br>Thanks &amp; regards</div>", MailFormat.ToHtml("Hi Thandi,\n\nThanks & regards"));
    }

    [Fact]
    public void Bold_and_italic()
    {
        var html = MailFormat.ToHtml("Please pay **by Friday** and *not* later. 5 * 3 = 15");
        Assert.Contains("<b>by Friday</b>", html);
        Assert.Contains("<i>not</i>", html);
        Assert.Contains("5 * 3 = 15", html);
    }

    [Fact]
    public void Lists_become_lists()
    {
        var html = MailFormat.ToHtml("You need:\n- a logo\n- photos\nThen:\n1. sign\n2. pay");
        Assert.Contains("You need:<ul", html);
        Assert.Contains("<li>a logo</li><li>photos</li></ul>Then:<ol", html);
        Assert.Contains("<li>sign</li><li>pay</li></ol>", html);
    }

    [Fact]
    public void Links_and_bare_addresses()
    {
        var html = MailFormat.ToHtml("See [the site](https://karoo.co.za/new?a=1&b=2). Or www.webignite.co.za.");
        Assert.Contains("<a href=\"https://karoo.co.za/new?a=1&amp;b=2\" style=\"color:#1a5fb4\">the site</a>.", html);
        Assert.Contains("<a href=\"https://www.webignite.co.za\" style=\"color:#1a5fb4\">www.webignite.co.za</a>.", html);
    }

    [Fact]
    public void Link_addresses_are_not_made_italic()
    {
        var html = MailFormat.ToHtml("https://x.co.za/*a*/b");
        Assert.DoesNotContain("<i>", html);
    }

    [Fact]
    public void Plain_text_part_shows_link_addresses()
    {
        Assert.Equal("See the site (https://karoo.co.za) **now**", MailFormat.ToText("See [the site](https://karoo.co.za) **now**"));
    }

    [Fact]
    public void Sent_mail_uses_the_formatting()
    {
        var mail = new OutgoingMail { AccountId = "a", Subject = "s", Body = "**Hi**\n- one" };
        Assert.Contains("<b>Hi</b>", Replies.HtmlPart(mail));
        Assert.Equal("**Hi**\n- one", Replies.TextPart(mail));
    }
}
