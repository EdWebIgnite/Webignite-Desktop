using Webignite.Core.Tools;

namespace Webignite.Tests;

public sealed class WebHelpersTests
{
    [Fact]
    public void Filler_text_is_the_same_every_time_and_the_size_asked_for()
    {
        var three = LoremIpsum.Make(LoremKind.Paragraphs, 3);
        Assert.Equal(three, LoremIpsum.Make(LoremKind.Paragraphs, 3));
        Assert.Equal(3, three.Split("\n\n").Length);
        Assert.StartsWith(LoremIpsum.Opening, three);

        var words = LoremIpsum.Make(LoremKind.Words, 12);
        Assert.Equal(12, words.Split(' ').Length);
        Assert.StartsWith("Lorem ipsum dolor sit amet", words);
        Assert.Equal(5, LoremIpsum.Make(LoremKind.Words, 5).Split(' ').Length);

        var sentences = LoremIpsum.Make(LoremKind.Sentences, 4, classicStart: false);
        Assert.Equal(4, sentences.Count(c => c == '.'));
        Assert.False(sentences.StartsWith("Lorem ipsum dolor", StringComparison.Ordinal));
        Assert.True(char.IsUpper(sentences[0]));
        Assert.DoesNotContain(" .", sentences);
        Assert.DoesNotContain(",.", sentences);
    }

    [Fact]
    public void Filler_text_stays_within_bounds()
    {
        Assert.Single(LoremIpsum.Make(LoremKind.Paragraphs, 0).Split("\n\n"));
        Assert.Equal(LoremIpsum.Most(LoremKind.Paragraphs), LoremIpsum.Make(LoremKind.Paragraphs, 5000).Split("\n\n").Length);
        Assert.Equal(LoremIpsum.Most(LoremKind.Words), LoremIpsum.Make(LoremKind.Words, 5000).Split(' ').Length);
    }

    [Theory]
    [InlineData("24", "1.5rem")]
    [InlineData("24px", "1.5rem")]
    [InlineData(" 18 PX ", "1.125rem")]
    [InlineData("1.5rem", "24px")]
    [InlineData("1,5rem", "24px")]
    [InlineData("0.875em", "14px")]
    [InlineData("10", "0.625rem")]
    [InlineData("-8px", "-0.5rem")]
    public void Pixels_and_rem_go_both_ways(string typed, string expected) => Assert.Equal(expected, CssUnits.Convert(typed));

    [Fact]
    public void Sizes_follow_the_base_and_nonsense_gives_nothing()
    {
        Assert.Equal("2rem", CssUnits.Convert("20px", 10));
        Assert.Equal("15px", CssUnits.Convert("1.5rem", 10));
        Assert.Equal("24px = 1.5rem", CssUnits.Sum("24"));
        Assert.Equal("1.5rem = 24px", CssUnits.Sum("1.5rem"));
        Assert.Null(CssUnits.Convert("wide"));
        Assert.Null(CssUnits.Convert(""));
        Assert.Null(CssUnits.Convert("24px", 0));
        Assert.Null(CssUnits.Sum("1.2.3"));
        Assert.Equal(17.5, CssUnits.Base("17,5"));
        Assert.Equal(18, CssUnits.Base("18px"));
        Assert.Null(CssUnits.Base("0"));
        Assert.Null(CssUnits.Base("1rem"));
    }

    [Fact]
    public void A_stylesheet_loses_its_comments_and_spare_space()
    {
        const string css = """
            /* The header */
            .site-header , .site-footer {
                color : #333;
                margin: 0 auto;   /* centred */
            }

            @media (min-width: 600px) {
                a:hover > span { font-family: "Open Sans", Arial; }
            }
            """;
        Assert.Equal(
            ".site-header,.site-footer{color :#333;margin:0 auto}@media (min-width:600px){a:hover > span{font-family:\"Open Sans\",Arial}}",
            Minify.Css(css));
    }

    [Fact]
    public void A_stylesheet_keeps_what_matters()
    {
        // Space inside calc and between selectors means something; quotes and urls are left alone.
        Assert.Equal(".a .b{width:calc(100% - 2rem)}", Minify.Css(".a  .b {\n width: calc(100% - 2rem);\n}"));
        Assert.Equal("a::after{content:\"  /* not a comment */  \"}", Minify.Css("a::after { content: \"  /* not a comment */  \"; }"));
        Assert.Equal("a{background:url(//cdn.example.com/a b.png)}", Minify.Css("a { background: url( //cdn.example.com/a b.png ); }"));
        Assert.Equal("/*! Licence */a{b:c}", Minify.Css("/*! Licence */\na { b: c; }"));
        Assert.Equal("", Minify.Css(null));
        Assert.Equal("a{b:c}", Minify.Css("a{b:c} /* never closed"));
    }

    [Fact]
    public void A_script_loses_comments_indents_and_empty_lines_but_keeps_its_line_breaks()
    {
        const string js = """
            // Opens the menu
            function open(menu) {
                /* one line */ menu.hidden = false;   // show it

                return menu
            }
            """;
        Assert.Equal("function open(menu) {\nmenu.hidden = false;\nreturn menu\n}", Minify.Js(js));
    }

    [Fact]
    public void A_script_keeps_what_is_inside_quotes_back_ticks_and_patterns()
    {
        Assert.Equal("var a = \"http://x.co.za  // not a comment\";", Minify.Js("var a = \"http://x.co.za  // not a comment\";  // gone"));
        Assert.Equal("var b = 'it\\'s // fine';", Minify.Js("var b = 'it\\'s // fine';"));
        Assert.Equal("var c = `line one\n    // kept ${ a + b } too\n  end`;", Minify.Js("var c = `line one\n    // kept ${ a   +   b /* gone */ } too\n  end`;  // gone"));
        Assert.Equal("var d = /[/]\\/\\/ x/g.test(s);", Minify.Js("var d = /[/]\\/\\/ x/g.test(s); // gone"));
        Assert.Equal("var e = a / b / c;", Minify.Js("var e = a / b / c; // divide, not a pattern"));
        Assert.Equal("return /\\s+$/.test(x)", Minify.Js("   return /\\s+$/.test(x)"));
        Assert.Equal("var f = `a ${ `b ${ {c: 1}.c } // in` } // out`;", Minify.Js("var f = `a ${ `b ${ {c: 1}.c } // in` } // out`;"));
    }

    [Fact]
    public void A_script_keeps_licences_and_breaks_a_line_where_a_comment_did()
    {
        Assert.Equal("/*! MIT */\nvar a = 1", Minify.Js("/*! MIT */\n\n\nvar a = 1"));
        Assert.Equal("a\nb", Minify.Js("a /* over\n two lines */ b"));
        Assert.Equal("a b", Minify.Js("a /* on one */ b"));
        Assert.Equal("a\nb", Minify.Js("a\r\n\r\n   b\r\n"));
        Assert.Equal("", Minify.Js(null));
        Assert.Equal("a", Minify.Js("a /* never closed"));
    }

    [Fact]
    public void Saved_is_a_whole_percentage()
    {
        Assert.Equal(50, Minify.Saved("aaaa", "aa"));
        Assert.Equal(0, Minify.Saved("aa", "aa"));
        Assert.Equal(0, Minify.Saved("", ""));
        Assert.Equal(0, Minify.Saved("a", "aaa"));
    }

    [Fact]
    public void Json_is_laid_out_or_put_on_one_line()
    {
        const string json = "{\"name\":\"Café Süd\",\"tags\":[\"a\",\"b\"],\"price\":1250.50,\"open\":true,\"note\":null}";
        Assert.Equal(
            "{\n  \"name\": \"Café Süd\",\n  \"tags\": [\n    \"a\",\n    \"b\"\n  ],\n  \"price\": 1250.50,\n  \"open\": true,\n  \"note\": null\n}",
            JsonTidy.Tidy(json).Text);
        Assert.Equal(json, JsonTidy.Tidy(JsonTidy.Tidy(json).Text, oneLine: true).Text);
        Assert.StartsWith("{\n    \"name\"", JsonTidy.Tidy(json, indent: 4).Text);
        Assert.True(JsonTidy.Tidy("  ").Ok);
        Assert.Equal("", JsonTidy.Tidy(null).Text);
    }

    [Fact]
    public void Broken_json_says_where()
    {
        var broken = JsonTidy.Tidy("{\n  \"a\": 1,\n  \"b\": oops\n}");
        Assert.False(broken.Ok);
        Assert.Equal("", broken.Text);
        Assert.StartsWith("This isn't valid JSON yet. Look at line 3,", broken.Problem);
    }

    [Theory]
    [InlineData("Karoo Coffee: Our Menu & Prices!", "karoo-coffee-our-menu-prices")]
    [InlineData("  Café Süd — né 1998  ", "cafe-sud-ne-1998")]
    [InlineData("Eddie's Web Design", "eddies-web-design")]
    [InlineData("---", "")]
    [InlineData("already-a-slug", "already-a-slug")]
    public void A_slug_is_safe_in_a_web_address(string typed, string expected) => Assert.Equal(expected, TextCases.Slug(typed));

    [Fact]
    public void The_same_words_every_way()
    {
        var all = TextCases.All(" the price of a new site ").ToDictionary(c => c.Name, c => c.Text);
        Assert.Equal("the-price-of-a-new-site", all["Slug"]);
        Assert.Equal("The Price of a New Site", all["Title Case"]);
        Assert.Equal("The price of a new site", all["Sentence case"]);
        Assert.Equal("the price of a new site", all["lower case"]);
        Assert.Equal("THE PRICE OF A NEW SITE", all["UPPER CASE"]);
        Assert.Equal("thePriceOfANewSite", all["camelCase"]);
        Assert.Equal("ThePriceOfANewSite", all["PascalCase"]);
        Assert.Equal("the_price_of_a_new_site", all["snake_case"]);
        Assert.Equal("the-price-of-a-new-site", all["kebab-case"]);
        Assert.Empty(TextCases.All("   "));
    }

    [Fact]
    public void Words_are_found_in_names_from_code_too()
    {
        Assert.Equal(["site", "name"], TextCases.Words("siteName"));
        Assert.Equal(["html", "page", "title"], TextCases.Words("HTMLPageTitle"));
        Assert.Equal(["main", "nav", "2", "items"], TextCases.Words("main_nav-2 items"));
        Assert.Equal("mainNav", TextCases.Camel("Main nav"));
        Assert.Equal("First one. Then two! And three", TextCases.Sentence("FIRST ONE. THEN TWO! AND THREE"));
        Assert.Equal("To Have and to Hold On", TextCases.Title("to have AND to hold on"));
    }

    [Fact]
    public void A_campaign_link_carries_its_tags()
    {
        Assert.Equal(
            "https://karoocoffee.co.za/?utm_source=newsletter&utm_medium=email&utm_campaign=spring%20sale",
            UtmLinks.Build("karoocoffee.co.za", "newsletter", "email", " spring sale ").Link);
        Assert.Equal(
            "https://x.co.za/menu?page=2&utm_source=facebook&utm_content=blue%20button#top",
            UtmLinks.Build("https://x.co.za/menu?page=2&utm_source=old&utm_medium=old#top", "facebook", "", null, content: "blue button").Link);
        Assert.Equal("http://x.co.za/a/?utm_source=qr", UtmLinks.Build("http://x.co.za/a/", "qr", null, null).Link);
    }

    [Fact]
    public void A_campaign_link_says_what_is_missing()
    {
        Assert.True(UtmLinks.Build("  ", "x", "y", "z").Ok);
        Assert.Equal("", UtmLinks.Build("", "x", "y", "z").Link);
        Assert.Equal("That doesn't look like a web address.", UtmLinks.Build("not an address", "x", "y", "z").Problem);
        Assert.Equal("That doesn't look like a web address.", UtmLinks.Build("ftp://x.co.za", "x", "y", "z").Problem);
        Assert.StartsWith("Add a source", UtmLinks.Build("x.co.za", " ", "email", "sale").Problem);
    }

    [Fact]
    public void A_placeholder_has_a_size_a_label_and_writing_that_shows()
    {
        Assert.Equal((600, 400), Placeholders.Size(600, 400));
        Assert.Equal((1, Placeholders.Most), Placeholders.Size(0, 99999));
        Assert.Equal("600 × 400", Placeholders.Label(600, 400));
        Assert.Equal("Hero picture", Placeholders.Label(600, 400, " Hero picture "));
        Assert.Equal("placeholder-1200x630.png", Placeholders.FileName(1200, 630));
        Assert.Equal(new Rgb(0, 0, 0), Placeholders.Ink(Colours.Parse(Placeholders.UsualColour)!.Value));
        Assert.Equal(new Rgb(255, 255, 255), Placeholders.Ink(new Rgb(20, 20, 60)));
        // Fits across a wide picture, and never taller than a shallow one.
        Assert.True(Placeholders.FontSize(1200, 630, "1200 × 630") * 0.55 * 10 <= 1200);
        Assert.Equal(20, Placeholders.FontSize(1200, 50, "A"));
    }
}
