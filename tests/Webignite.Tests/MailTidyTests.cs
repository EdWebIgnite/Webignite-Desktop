using Webignite.Core.Mail;

namespace Webignite.Tests;

public class MailTidyTests
{
    [Fact]
    public void A_picture_sized_in_inches_by_Word_gets_its_size_in_pixels()
    {
        var tidy = MailTidy.Tidy("<img border=0 width=240 height=84 style='width:2.5in;height:.875in' src=\"cid:logo\">");
        Assert.Equal("<img border=0 width=240 height=84 src=\"cid:logo\" style=\"width:240px;height:84px\">", tidy);
    }

    [Fact]
    public void A_wide_picture_shrinks_to_fit_and_keeps_its_shape()
    {
        var tidy = MailTidy.Tidy("<a href=\"#\"><img src=\"a.png\" width=\"600\" height=\"250\" style=\"display:block;max-width:600px\" /></a>");
        Assert.Equal("<a href=\"#\"><table cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;border-collapse:collapse\"><tr><td style=\"padding:0\">"
            + "<img src=\"a.png\" style=\"display:block;width:100%;height:auto\"></td></tr></table></a>", tidy);
    }

    [Fact]
    public void A_big_picture_pasted_in_with_no_size_given_is_measured_and_made_to_fit()
    {
        // The start of a PNG 1200 wide: enough to read its size from.
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0x04, 0xB0, 0, 0, 0x01, 0xA4, 8, 2, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(1200, MailTidy.PictureWidth(png));
        var tidy = MailTidy.Tidy($"<img src=\"data:image/png;base64,{Convert.ToBase64String(png)}\">");
        Assert.Contains("max-width:1200px", tidy);
        Assert.Contains("width:100%;height:auto", tidy);
    }

    [Fact]
    public void A_small_picture_with_no_size_given_is_left_alone()
    {
        const string html = "<img src=\"https://example.co.za/logo.png\" alt=\"Logo\">";
        Assert.Equal(html, MailTidy.Tidy(html));
    }

    [Fact]
    public void A_table_made_for_a_browser_takes_the_room_there_is_up_to_its_own_width()
    {
        var tidy = MailTidy.Tidy("<table class=\"container\" width=\"600\" style=\"width:600px;max-width:600px;background:#ffffff\"><tr><td width=\"600\">Hi</td><td width=50%>x</td></tr></table>");
        Assert.StartsWith("<table class=\"container\" style=\"background:#ffffff;width:100%;max-width:600px\">", tidy);
        Assert.Contains("<td>Hi</td>", tidy);
        Assert.Contains("<td width=50%>x</td>", tidy);
    }

    [Fact]
    public void Columns_of_a_table_that_gave_way_share_the_room_and_gaps_keep_their_width()
    {
        var tidy = MailTidy.Tidy("<table width=560><tr><td width=300 style='width:225.0pt;padding:0'>Name</td><td width=20></td><td width=240>Logo</td></tr></table><table><tr><td width=300>Kept</td></tr></table>");
        Assert.Equal("<table style=\"width:100%;max-width:560px\"><tr><td style=\"padding:0\">Name</td><td width=20></td><td>Logo</td></tr></table><table><tr><td width=300>Kept</td></tr></table>", tidy);
    }

    [Fact]
    public void A_small_table_such_as_a_signature_is_left_as_it_is() =>
        Assert.Equal("<table width=\"300\" cellpadding=\"0\"><tr><td>Sig</td></tr></table>", MailTidy.Tidy("<table width=\"300\" cellpadding=\"0\"><tr><td>Sig</td></tr></table>"));

    [Fact]
    public void Words_own_marks_go_and_the_blank_line_they_held_stays() =>
        Assert.Equal("<p class=MsoNormal><span lang=EN><br></span></p>", MailTidy.Tidy("<p class=MsoNormal><span lang=EN><o:p>&nbsp;</o:p></span></p>"));

    [Fact]
    public void A_spacer_keeps_its_height_and_never_pushes_the_mail_wider()
    {
        Assert.Equal("<img src=\"spacer.gif\" height=\"20\" style=\"width:100%\">", MailTidy.Tidy("<img src=\"spacer.gif\" width=\"600\" height=\"20\">"));
        const string narrow = "<img src=\"spacer.gif\" width=\"1\" height=\"300\">";
        Assert.Equal(narrow, MailTidy.Tidy(narrow));
    }

    [Fact]
    public void Notes_for_Outlook_alone_go_and_what_was_meant_for_everyone_else_stays()
    {
        var tidy = MailTidy.Tidy("A<!--[if mso]><table width=\"600\"><tr><td><![endif]-->B<!--[if !mso]><!--><b>C</b><!--<![endif]-->D<!--[if gte mso 9]><xml>x</xml><![endif]-->E");
        Assert.Equal("AB<b>C</b>DE", tidy);
    }

    [Theory]
    [InlineData(1, "in", 96)]
    [InlineData(2.54, "cm", 96)]
    [InlineData(72, "pt", 96)]
    public void Lengths_become_pixels(double number, string unit, double pixels) => Assert.Equal(pixels, MailTidy.Pixels(number, unit), 3);

    [Fact]
    public void Ordinary_mail_is_not_changed()
    {
        const string html = "<div dir=\"ltr\"><p>Hi Eddie,</p><blockquote style=\"margin:0 0 0 .8ex;border-left:1px solid #ccc\">Quoted</blockquote></div>";
        Assert.Equal(html, MailTidy.Tidy(html));
    }

    [Fact]
    public void A_link_dressed_as_a_button_becomes_one_the_viewer_can_draw()
    {
        var tidy = MailTidy.Tidy("<a href=\"https://example.com/q\" style=\"background-color:#4a0072;color:#ffffff;display:inline-block;height:48px;line-height:48px;padding:0 24px;text-decoration:none;border-radius:4px\">Review and approve</a>");
        Assert.Equal("<table cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td align=\"center\" valign=\"middle\" height=\"48\" style=\"background-color:#4a0072;padding:0 24px;border-radius:4px\">"
            + "<a href=\"https://example.com/q\" style=\"color:#ffffff;text-decoration:none\">Review and approve</a></td></tr></table>", tidy);
    }

    [Fact]
    public void A_button_made_with_padding_alone_keeps_its_padding()
    {
        var tidy = MailTidy.Tidy("<a href=\"#\" style=\"background:#d67828;color:#fff;padding:12px 24px;display:inline-block\"><b>Pay now</b></a>");
        Assert.Contains("<td align=\"center\" valign=\"middle\" style=\"background-color:#d67828;padding:12px 24px\">", tidy);
        Assert.Contains("<a href=\"#\" style=\"color:#fff\"><b>Pay now</b></a>", tidy);
    }

    [Fact]
    public void A_highlighted_link_and_a_plain_one_stay_links()
    {
        const string html = "<a href=\"#\" style=\"background-color:yellow\">note</a> and <a href=\"#\" style=\"color:#1a73e8\">site</a>";
        Assert.Equal(html, MailTidy.Tidy(html));
    }
}
