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
    public void Wrap_adds_and_removes_markers()
    {
        var on = MailFormat.Wrap("pay by Friday", 7, 13, "**");
        Assert.Equal(new FormatEdit("pay by **Friday**", 9, 15), on);
        var off = MailFormat.Wrap(on.Text, on.SelectionStart, on.SelectionEnd, "**");
        Assert.Equal(new FormatEdit("pay by Friday", 7, 13), off);
    }

    [Fact]
    public void Wrap_leaves_spaces_outside()
    {
        Assert.Equal("pay **by** Friday", MailFormat.Wrap("pay by Friday", 4, 7, "**").Text);
    }

    [Fact]
    public void Wrap_with_nothing_selected_puts_the_caret_between()
    {
        Assert.Equal(new FormatEdit("Hi ****", 5, 5), MailFormat.Wrap("Hi ", 3, 3, "**"));
    }

    [Fact]
    public void Italic_inside_bold_does_not_unwrap_the_bold()
    {
        Assert.Equal("***Friday***", MailFormat.Wrap("**Friday**", 2, 8, "*").Text);
    }

    [Fact]
    public void List_toggles_the_selected_lines()
    {
        var text = "Intro\nlogo\nphotos\nEnd";
        var on = MailFormat.List(text, 8, 14, numbered: false);
        Assert.Equal("Intro\n- logo\n- photos\nEnd", on.Text);
        Assert.Equal(text, MailFormat.List(on.Text, on.SelectionStart, on.SelectionEnd, numbered: false).Text);
        Assert.Equal("Intro\n1. logo\n2. photos\nEnd", MailFormat.List(on.Text, on.SelectionStart, on.SelectionEnd, numbered: true).Text);
    }

    [Fact]
    public void List_on_an_empty_line_starts_one()
    {
        var edit = MailFormat.List("Hi\n", 3, 3, numbered: true);
        Assert.Equal(new FormatEdit("Hi\n1. ", 6, 6), edit);
    }

    [Theory]
    [InlineData("webignite.co.za", "[webignite.co.za](https://webignite.co.za)")]
    [InlineData("https://x.co.za", "[x.co.za](https://x.co.za)")]
    [InlineData("eddie@webignite.co.za", "[eddie@webignite.co.za](mailto:eddie@webignite.co.za)")]
    public void Link_with_nothing_selected_uses_the_address(string url, string expected)
    {
        Assert.Equal(expected, MailFormat.Link("", 0, 0, url).Text);
    }

    [Fact]
    public void Link_wraps_the_selection()
    {
        var edit = MailFormat.Link("Pay online here", 11, 15, "in.xero.com/abc");
        Assert.Equal("Pay online [here](https://in.xero.com/abc)", edit.Text);
        Assert.Equal(edit.Text.Length, edit.SelectionStart);
    }

    [Fact]
    public void Sent_mail_uses_the_formatting()
    {
        var mail = new OutgoingMail { AccountId = "a", Subject = "s", Body = "**Hi**\n- one" };
        Assert.Contains("<b>Hi</b>", Replies.HtmlPart(mail));
        Assert.Equal("**Hi**\n- one", Replies.TextPart(mail));
    }
}
