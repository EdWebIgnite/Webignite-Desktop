using Webignite.Core.Tools;
using Webignite.Tools;

namespace Webignite.Tests;

public sealed class BrandSheetTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "webignite-brand-" + Guid.NewGuid().ToString("N"));

    public BrandSheetTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static readonly IReadOnlyList<Swatch> Palette =
    [
        new("Primary", "#E8590C"), new("Ink", "#3D2B1F"), new("Paper", "#F8F5F0"), new("Broken", "orange"), new("Sky", "#1B6CA8"),
    ];

    [Fact]
    public void A_sheet_needs_a_name_and_something_to_show()
    {
        Assert.StartsWith("Say whose brand", BrandSheet.Problem(new BrandSheetContent { Colours = Palette }));
        Assert.StartsWith("There is nothing to put on the sheet", BrandSheet.Problem(new BrandSheetContent { Name = "Karoo Coffee" }));
        Assert.Null(BrandSheet.Problem(new BrandSheetContent { Name = "Karoo Coffee", Colours = Palette }));
        Assert.Null(BrandSheet.Problem(new BrandSheetContent { Name = "Karoo Coffee", BodyFont = "Open Sans" }));
        Assert.Equal("Karoo Coffee brand sheet.pdf", BrandSheet.FileName(" Karoo Coffee "));
        Assert.Equal("Brand brand sheet.pdf", BrandSheet.FileName(null));
    }

    [Fact]
    public void Only_real_colours_go_on_the_sheet_and_no_more_than_fit()
    {
        Assert.Equal(["Primary", "Ink", "Paper", "Sky"], BrandSheet.Shown(Palette).Select(s => s.Swatch.Name));
        var many = Enumerable.Range(0, 12).Select(i => new Swatch("c" + i, $"#0000{i:X2}")).ToList();
        Assert.Equal(BrandSheet.MostColours, BrandSheet.Shown(many).Count);
    }

    [Fact]
    public void Each_colour_has_its_codes_for_web_screen_and_print()
    {
        Assert.Equal(["#E8590C", "RGB 232, 89, 12", "CMYK 0, 62, 95, 9"], BrandSheet.Codes(new Rgb(0xE8, 0x59, 0x0C)));
        Assert.True(BrandSheet.WhiteOn(new Rgb(0x3D, 0x2B, 0x1F)));
        Assert.False(BrandSheet.WhiteOn(new Rgb(0xF8, 0xF5, 0xF0)));
    }

    [Fact]
    public void Typefaces_the_pc_doesnt_have_are_named()
    {
        Assert.Null(BrandSheet.MissingFonts([]));
        Assert.StartsWith("Montserrat isn't installed", BrandSheet.MissingFonts(["Montserrat"]));
        Assert.StartsWith("Montserrat and Open Sans aren't installed", BrandSheet.MissingFonts(["Montserrat", "Open Sans"]));
    }

    [Fact]
    public void The_sheet_is_one_pdf_page_with_the_name_and_the_codes_as_real_text()
    {
        var logo = Path.Combine(root, "karoo.svg");
        File.WriteAllText(logo, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" width="200" height="100"><circle cx="100" cy="50" r="40" fill="#E8590C"/></svg>""");
        var pdf = Path.Combine(root, "sheet.pdf");

        var missing = BrandSheetMaker.Write(new BrandSheetContent
        {
            Name = "Karoo Coffee", LogoPath = logo, Colours = Palette, HeadingFont = "A Typeface Nobody Has", BodyFont = "", PreparedBy = "Webignite",
        }, pdf);

        var bytes = File.ReadAllBytes(pdf);
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
        Assert.Equal(1, PdfText.Pages(bytes));
        var words = PdfText.Read(bytes);
        Assert.Contains("Karoo Coffee", words, StringComparison.Ordinal);
        Assert.Contains("#E8590C", words, StringComparison.Ordinal);
        Assert.Contains("CMYK 0, 62, 95, 9", words, StringComparison.Ordinal);
        Assert.Contains("Prepared by Webignite", words, StringComparison.Ordinal);
        Assert.DoesNotContain("Broken", words, StringComparison.Ordinal);
        Assert.Equal(["A Typeface Nobody Has"], missing);
    }

    [Fact]
    public void A_sheet_with_only_colours_is_still_made()
    {
        var pdf = Path.Combine(root, "colours.pdf");
        Assert.Empty(BrandSheetMaker.Write(new BrandSheetContent { Name = "Karoo", Colours = Palette }, pdf));
        Assert.Equal(1, PdfText.Pages(File.ReadAllBytes(pdf)));
    }
}
