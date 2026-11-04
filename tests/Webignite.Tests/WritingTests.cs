using Webignite.Core.Writing;

namespace Webignite.Tests;

/// <summary>The mail editor's writing: typing, deleting, formatting, and the HTML and text it becomes.</summary>
public sealed class WritingTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAYAAAC56t6BAAAAEklEQVR4nGP4z8DwH4QZYAwAR8oH+Rq28akAAAAASUVORK5CYII=");

    private static RichDocument Doc(params string[] paragraphs)
    {
        var document = new RichDocument();
        document.Blocks.AddRange(paragraphs.Select(p => new TextBlock(p)));
        document.EnsureNotEmpty();
        return document;
    }

    private static string Texts(RichDocument d) => string.Join("|", d.Blocks.Select(b => b is TextBlock t ? t.Text : b.GetType().Name));

    [Fact]
    public void Typing_and_enter_make_paragraphs()
    {
        var d = RichDocument.Empty();
        var at = RichEditing.Insert(d, RichEditing.Start, "Hi Thandi,", default);
        at = RichEditing.Enter(d, at);
        at = RichEditing.Insert(d, at, "Thanks.\nSpeak soon", default);
        Assert.Equal("Hi Thandi,|Thanks.|Speak soon", Texts(d));
        Assert.Equal(new DocPosition(2, 10), at);
    }

    [Fact]
    public void Enter_in_the_middle_splits_and_backspace_joins_again()
    {
        var d = Doc("Hello there");
        var at = RichEditing.Enter(d, new DocPosition(0, 5));
        Assert.Equal("Hello| there", Texts(d));
        at = RichEditing.Backspace(d, at);
        Assert.Equal("Hello there", Texts(d));
        Assert.Equal(new DocPosition(0, 5), at);
    }

    [Fact]
    public void Backspace_keeps_an_emoji_whole()
    {
        var d = Doc("Thanks 👍");
        var at = RichEditing.Backspace(d, new DocPosition(0, ((TextBlock)d.Blocks[0]).Length));
        Assert.Equal("Thanks ", Texts(d));
        Assert.Equal(7, at.Offset);
    }

    [Fact]
    public void Backspace_at_the_start_of_a_list_item_takes_the_bullet_off_first()
    {
        var d = Doc("one");
        ((TextBlock)d.Blocks[0]).List = ListKind.Bullet;
        RichEditing.Backspace(d, new DocPosition(0, 0));
        Assert.Equal(ListKind.None, ((TextBlock)d.Blocks[0]).List);
    }

    [Fact]
    public void Enter_on_an_empty_list_item_ends_the_list()
    {
        var d = Doc("one");
        RichEditing.ToggleList(d, new DocPosition(0, 0), new DocPosition(0, 0), ListKind.Bullet);
        var at = RichEditing.Enter(d, new DocPosition(0, 3));
        Assert.Equal(ListKind.Bullet, ((TextBlock)d.Blocks[1]).List);
        RichEditing.Enter(d, at);
        Assert.Equal(ListKind.None, ((TextBlock)d.Blocks[1]).List);
        Assert.Equal(2, d.Blocks.Count);
    }

    [Fact]
    public void Deleting_across_paragraphs_joins_what_is_left()
    {
        var d = Doc("Dear Sir", "middle", "Kind regards");
        var at = RichEditing.Delete(d, new DocPosition(0, 5), new DocPosition(2, 5));
        Assert.Equal("Dear regards", Texts(d));
        Assert.Equal(new DocPosition(0, 5), at);
    }

    [Fact]
    public void Formatting_part_of_a_word_splits_it_into_runs()
    {
        var d = Doc("Pay by Friday");
        RichEditing.Format(d, new DocPosition(0, 7), new DocPosition(0, 13), f => f with { Bold = true });
        var runs = ((TextBlock)d.Blocks[0]).Runs;
        Assert.Equal(["Pay by ", "Friday"], runs.Select(r => r.Text));
        Assert.True(runs[1].Format.Bold);
        Assert.True(RichEditing.FormatOf(d, new DocPosition(0, 8), new DocPosition(0, 10)).Bold);
        Assert.True(RichEditing.FormatAt(d, new DocPosition(0, 13)).Bold); // typing on carries on bold
    }

    [Fact]
    public void Remove_formatting_keeps_links()
    {
        var f = new RunFormat(Bold: true, Colour: "#ff0000", Link: "https://x.co.za");
        Assert.Equal(new RunFormat(Link: "https://x.co.za"), f.Cleared());
    }

    [Fact]
    public void A_picture_goes_on_its_own_line_and_typing_carries_on_after_it()
    {
        var d = Doc("Before after");
        var at = RichEditing.InsertBlock(d, new DocPosition(0, 6), new PictureBlock(Png, "image/png", 2, 3));
        Assert.Equal("Before|PictureBlock| after", Texts(d));
        Assert.Equal(new DocPosition(2, 0), at);
        at = RichEditing.Backspace(d, at); // removes the picture before it
        Assert.Equal("Before| after", Texts(d));
    }

    [Fact]
    public void A_table_takes_the_caret_to_its_first_cell_and_tab_like_moves_go_cell_to_cell()
    {
        var d = Doc("Prices");
        var at = RichEditing.InsertBlock(d, new DocPosition(0, 6), TableBlock.Empty(2, 2));
        Assert.Equal(1, at.Slot);
        RichEditing.Insert(d, at, "Hosting", default);
        var slots = RichEditing.Slots(d);
        Assert.Equal(6, slots.Count); // paragraph, four cells, paragraph after
        Assert.True(slots[1].InTable);
        RichEditing.Enter(d, new DocPosition(1, 7)); // a line break inside the cell, not a new row
        Assert.Equal("Hosting\n", slots[1].Text!.Text);
    }

    [Fact]
    public void Pasting_several_paragraphs_in_the_middle_of_one()
    {
        var d = Doc("Start end");
        var at = RichEditing.InsertDocument(d, new DocPosition(0, 6), Doc("one", "two"));
        Assert.Equal("Start one|twoend", Texts(d));
        Assert.Equal(new DocPosition(1, 3), at);
    }

    [Fact]
    public void Copy_takes_just_the_selection()
    {
        var d = Doc("Dear Sir", "Kind regards");
        var copy = RichEditing.Copy(d, new DocPosition(0, 5), new DocPosition(1, 4));
        Assert.Equal("Sir|Kind", Texts(copy));
    }

    [Fact]
    public void Changing_mailbox_swaps_the_signature_in_place()
    {
        var d = RichText.Starting("Hi there", RichText.Signature("Eddie\nWebignite", null, null));
        Assert.Equal("Hi there||Eddie|Webignite", Texts(d));
        d.Blocks.Add(new TextBlock("PS"));
        Assert.True(RichEditing.SwapSignature(d, RichText.Signature("Accounts", Png, "image/png")));
        Assert.Equal("Hi there||Accounts|PictureBlock|PS", Texts(d));
        Assert.Equal(2, d.Blocks.Count(b => b.IsSignature));
    }

    [Fact]
    public void A_new_mail_starts_with_room_to_type_above_the_signature()
    {
        var d = RichText.Starting(null, RichText.Signature("Eddie", null, null));
        Assert.Equal("||Eddie", Texts(d));
        Assert.True(d.IsBlank);
    }

    [Fact]
    public void Old_marked_up_text_reads_as_formatting()
    {
        var d = RichText.FromText("Hi **Thandi**, see [the site](https://karoo.co.za)\n- logo\n- photos");
        var first = (TextBlock)d.Blocks[0];
        Assert.Contains(first.Runs, r => r.Text == "Thandi" && r.Format.Bold);
        Assert.Contains(first.Runs, r => r.Text == "the site" && r.Format.Link == "https://karoo.co.za");
        Assert.Equal(ListKind.Bullet, ((TextBlock)d.Blocks[1]).List);
        Assert.Equal("logo", ((TextBlock)d.Blocks[1]).Text);
    }

    [Fact]
    public void The_text_part_keeps_lists_links_and_tables_readable()
    {
        var d = RichText.FromText("Hi\n1. one\n1. two\nsee [the site](https://karoo.co.za)");
        var table = TableBlock.Empty(1, 2);
        table.Rows[0][0].Insert(0, "Hosting", default);
        table.Rows[0][1].Insert(0, "R 450", default);
        d.Blocks.Add(table);
        Assert.Equal("Hi\n1. one\n2. two\nsee the site (https://karoo.co.za)\nHosting | R 450", RichText.ToText(d));
    }

    [Fact]
    public void Html_round_trip_keeps_every_kind_of_formatting()
    {
        var d = new RichDocument();
        var p = new TextBlock("Plain ") { Align = Alignment.Centre, Indent = 1 };
        p.Insert(6, "bold", new RunFormat(Bold: true, Italic: true, Underline: true));
        p.Insert(10, " red", new RunFormat(Colour: "#c0392b", Highlight: "#fff3a0", Size: TextSize.Large));
        p.Insert(14, " code", new RunFormat(Fixed: true));
        p.Insert(19, " link", new RunFormat(Link: "https://webignite.co.za"));
        d.Blocks.Add(p);
        d.Blocks.Add(new TextBlock("Heading") { Style = BlockStyle.Heading2 });
        d.Blocks.Add(new TextBlock("Quoted") { Style = BlockStyle.Quote });
        d.Blocks.Add(new TextBlock("x = 1") { Style = BlockStyle.Code });
        d.Blocks.Add(new TextBlock("first") { List = ListKind.Number });
        d.Blocks.Add(new TextBlock("second") { List = ListKind.Number });
        d.Blocks.Add(new TextBlock());
        d.Blocks.Add(new RuleBlock());
        var table = TableBlock.Empty(2, 2);
        table.Rows[1][1].Insert(0, "R 450", default);
        d.Blocks.Add(table);
        d.Blocks.Add(new TextBlock("Eddie") { IsSignature = true });
        d.Blocks.Add(new PictureBlock(Png, "image/png", 2, 3) { IsSignature = true, Align = Alignment.Right });

        var html = RichHtml.Write(d, RichHtml.ToDataAddress);
        var back = RichHtml.Read(html);

        Assert.Equal(RichHtml.Write(back, RichHtml.ToDataAddress), html);
        Assert.Equal(Texts(d), Texts(back));
        var runs = ((TextBlock)back.Blocks[0]).Runs;
        Assert.Equal(d.Blocks.Count, back.Blocks.Count);
        Assert.Equal(((TextBlock)d.Blocks[0]).Runs, runs);
        Assert.True(back.Blocks[^1].IsSignature);
        Assert.Equal(Alignment.Right, ((PictureBlock)back.Blocks[^1]).Align);
    }

    [Fact]
    public void Html_from_other_programs_reads_sensibly()
    {
        var d = RichHtml.Read("""
            <html><head><style>p{color:red}</style></head><body>
            <div>Hi <strong>Pieter</strong>,</div><div><br></div>
            <div>Line one<br>Line two</div>
            <ul><li>a</li><li>b<ul><li>c</li></ul></li></ul>
            <p style="text-align:center"><font color="#00f" size="5">big blue</font></p>
            <img src="https://tracker.example/x.png">
            </body></html>
            """);
        Assert.Equal("Hi Pieter,||Line one\nLine two|a|b|c|big blue", Texts(d));
        Assert.Equal(1, ((TextBlock)d.Blocks[5]).Indent);
        var big = ((TextBlock)d.Blocks[6]).Runs[0].Format;
        Assert.Equal("#0000ff", big.Colour);
        Assert.Equal(TextSize.ExtraLarge, big.Size);
        Assert.Equal(Alignment.Centre, ((TextBlock)d.Blocks[6]).Align);
    }

    [Fact]
    public void Pictures_by_content_id_are_found_and_web_pictures_never_fetched()
    {
        var d = RichHtml.Read("<p>x</p><img src=\"cid:logo1\" width=\"120\"><img src=\"http://x/y.png\">",
            src => src == "cid:logo1" ? (Png, "image/png") : null);
        var picture = Assert.IsType<PictureBlock>(d.Blocks[1]);
        Assert.Equal(120, picture.Width);
        Assert.Equal(2, d.Blocks.Count);
    }

    [Theory]
    [InlineData("#ABC", "#aabbcc")]
    [InlineData("rgb(255, 0, 16)", "#ff0010")]
    [InlineData("Blue", "#0000ff")]
    [InlineData("nonsense", null)]
    public void Colours_read_from_mail_come_back_as_hex(string value, string? expected) => Assert.Equal(expected, RichHtml.Colour(value));

    [Fact]
    public void Picture_sizes_are_read_from_the_file()
    {
        Assert.Equal(new ImageSize.Size(2, 3, "image/png"), ImageSize.Of(Png));
        Assert.Null(ImageSize.Of([1, 2, 3]));
    }

    [Fact]
    public void Words_for_ctrl_arrows_and_double_click()
    {
        const string text = "Pay R 450 by Friday";
        Assert.Equal(13, RichEditing.WordStart(text, 19));
        Assert.Equal(13, RichEditing.WordEnd(text, 10));
        Assert.Equal((13, 19), RichEditing.WordAt(text, 15));
    }

    [Fact]
    public void Sizes_step_and_stop_at_the_ends()
    {
        Assert.Equal(TextSize.Large, TextSizes.Step(TextSize.Normal, 1));
        Assert.Equal(TextSize.Huge, TextSizes.Step(TextSize.Huge, 1));
        Assert.Equal(TextSize.Small, TextSizes.Nearest(9.5));
    }
}
