using System.IO.Compression;
using SkiaSharp;
using Webignite.Core.Tools;
using Webignite.Tools;

namespace Webignite.Tests;

public sealed class LogoPackTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "webignite-logos-" + Guid.NewGuid().ToString("N"));

    public LogoPackTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    // A square logo: an orange disc on nothing, twice as wide as tall once the box is counted.
    private string Svg()
    {
        var path = Path.Combine(root, "karoo.svg");
        File.WriteAllText(path, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" width="200" height="100"><circle cx="100" cy="50" r="40" fill="#E8590C"/></svg>""");
        return path;
    }

    private string Png(int width, int height, bool seeThrough)
    {
        var path = Path.Combine(root, seeThrough ? "clear.png" : "solid.png");
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(seeThrough ? SKColors.Transparent : SKColors.White);
            using var paint = new SKPaint { Color = new SKColor(0xE8, 0x59, 0x0C), IsAntialias = true };
            canvas.DrawCircle(width / 2f, height / 2f, height * 0.4f, paint);
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public void Names_become_file_names()
    {
        Assert.Equal("karoo-coffee-roasters", LogoPack.Stem(" Karoo Coffee  Roasters! "));
        Assert.Equal("logo", LogoPack.Stem("   "));
        Assert.Equal("Karoo Coffee logo pack.zip", LogoPack.ZipName("Karoo Coffee"));
        Assert.Equal("Logo logo pack.zip", LogoPack.ZipName(null));
    }

    [Fact]
    public void A_vector_is_made_at_every_width_and_a_picture_never_bigger_than_it_is()
    {
        Assert.Equal([2000, 1000, 500, 250], LogoPack.WidthsFor(64, vector: true));
        Assert.Equal([1000, 500, 250], LogoPack.WidthsFor(1400, vector: false));
        Assert.Equal([180], LogoPack.WidthsFor(180, vector: false));
    }

    [Fact]
    public void A_see_through_logo_gets_the_full_pack()
    {
        var files = LogoPack.Plan("Karoo Coffee", 600, vector: false, seeThrough: true);
        Assert.Equal(14, files.Count);
        Assert.Contains(files, f => f is { Path: "Colour/karoo-coffee-500px.png", Ink: LogoInk.Colour, Back: LogoBack.Transparent, Width: 500 });
        Assert.Contains(files, f => f is { Path: "Colour/karoo-coffee-250px.webp" });
        Assert.Contains(files, f => f is { Path: "Colour/karoo-coffee-on-white-500px.jpg", Back: LogoBack.White });
        Assert.Contains(files, f => f is { Path: "White/karoo-coffee-white-on-black-500px.jpg", Ink: LogoInk.White, Back: LogoBack.Black });
        Assert.Contains(files, f => f is { Path: "Black/karoo-coffee-black-250px.png", Ink: LogoInk.Black, Back: LogoBack.Transparent });
        Assert.Equal(files.Count, files.Select(f => f.Path).Distinct().Count());
    }

    [Fact]
    public void A_logo_on_a_solid_background_gets_only_the_colour_versions_and_says_why()
    {
        var files = LogoPack.Plan("Karoo", 600, vector: false, seeThrough: false);
        Assert.All(files, f => Assert.Equal((LogoInk.Colour, LogoBack.White), (f.Ink, f.Back)));
        Assert.Equal(4, files.Count);
        var warning = LogoPack.Warning(600, vector: false, seeThrough: false);
        Assert.Contains("600 pixels wide", warning, StringComparison.Ordinal);
        Assert.Contains("no see-through background", warning, StringComparison.Ordinal);
        Assert.Null(LogoPack.Warning(600, vector: true, seeThrough: true));
        Assert.StartsWith("It has no see-through background", LogoPack.Warning(600, vector: true, seeThrough: false));
        Assert.Null(LogoPack.Warning(3000, vector: false, seeThrough: true));
    }

    [Fact]
    public void The_note_in_the_pack_only_mentions_what_is_in_it()
    {
        var full = LogoPack.ReadMe("Karoo Coffee", vectorIncluded: true, seeThrough: true);
        Assert.StartsWith("Karoo Coffee logo files", full, StringComparison.Ordinal);
        Assert.Contains("Vector folder", full, StringComparison.Ordinal);
        Assert.Contains("White folder", full, StringComparison.Ordinal);
        var plain = LogoPack.ReadMe(null, vectorIncluded: false, seeThrough: false);
        Assert.DoesNotContain("Vector folder", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("White folder", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void An_svg_is_drawn_sharp_at_the_size_asked_in_colour_white_and_black()
    {
        using var master = LogoMaster.Open(Svg());
        Assert.True(master.IsVector && master.SeeThrough);
        Assert.Equal((200f, 100f), (master.Width, master.Height));

        using var colour = SKBitmap.Decode(LogoMaker.Make(master, new LogoFile("a.png", LogoInk.Colour, LogoBack.Transparent, 1000, "png")));
        Assert.Equal((1000, 500), (colour.Width, colour.Height));
        Assert.Equal(new SKColor(0xE8, 0x59, 0x0C), colour.GetPixel(500, 250));
        Assert.Equal(0, colour.GetPixel(5, 5).Alpha);

        using var white = SKBitmap.Decode(LogoMaker.Make(master, new LogoFile("w.png", LogoInk.White, LogoBack.Transparent, 400, "png")));
        Assert.Equal(SKColors.White, white.GetPixel(200, 100));
        Assert.Equal(0, white.GetPixel(2, 2).Alpha);

        using var onWhite = SKBitmap.Decode(LogoMaker.Make(master, new LogoFile("b.jpg", LogoInk.Black, LogoBack.White, 400, "jpg")));
        var middle = onWhite.GetPixel(200, 100);
        var corner = onWhite.GetPixel(2, 2);
        Assert.True(middle.Red < 20 && middle.Green < 20 && middle.Blue < 20);
        Assert.True(corner.Red > 240 && corner.Green > 240 && corner.Blue > 240);
    }

    [Fact]
    public void A_picture_knows_whether_its_background_is_see_through()
    {
        using var clear = LogoMaster.Open(Png(600, 300, seeThrough: true));
        using var solid = LogoMaster.Open(Png(600, 300, seeThrough: false));
        Assert.True(clear.SeeThrough);
        Assert.False(solid.SeeThrough);
        Assert.False(clear.IsVector);
        var broken = Path.Combine(root, "broken.svg");
        File.WriteAllText(broken, "not a logo");
        Assert.Throws<IOException>(() => LogoMaster.Open(broken));
        var notPicture = Path.Combine(root, "notes.png");
        File.WriteAllText(notPicture, "not a picture");
        Assert.Throws<IOException>(() => LogoMaster.Open(notPicture));
    }

    [Fact]
    public void An_svg_with_its_own_background_isnt_taken_as_see_through()
    {
        var path = Path.Combine(root, "boxed.svg");
        File.WriteAllText(path, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100"><rect width="100" height="100" fill="#3D2B1F"/><circle cx="50" cy="50" r="30" fill="#E8590C"/></svg>""");
        using var boxed = LogoMaster.Open(path);
        Assert.True(boxed.IsVector);
        Assert.False(boxed.SeeThrough);
        Assert.All(LogoPack.Plan("Boxed", (int)boxed.Width, boxed.IsVector, boxed.SeeThrough), f => Assert.Equal(LogoInk.Colour, f.Ink));
    }

    [Fact]
    public void The_pack_is_one_zip_with_every_file_the_svg_and_the_note()
    {
        using var master = LogoMaster.Open(Svg());
        var zipPath = Path.Combine(root, "pack.zip");

        var made = LogoMaker.WritePack(master, "Karoo Coffee", zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Equal(28, made);
        Assert.Equal(30, names.Count);
        Assert.Contains("Vector/karoo-coffee.svg", names);
        Assert.Contains("Read me.txt", names);
        Assert.Contains("Colour/karoo-coffee-2000px.png", names);
        Assert.Contains("White/karoo-coffee-white-on-black-250px.jpg", names);
        Assert.All(zip.Entries, e => Assert.True(e.Length > 0, e.FullName));
    }

    [Fact]
    public void The_favicon_set_has_every_icon_the_ico_the_manifest_and_the_lines_to_paste()
    {
        using var master = LogoMaster.Open(Svg());
        var folder = Path.Combine(root, "favicons");

        var html = LogoMaker.WriteFavicons(master, "Karoo Coffee Roasters", folder, new Rgb(255, 255, 255), new Rgb(0xE8, 0x59, 0x0C));

        foreach (var file in Favicons.Files)
        {
            using var icon = SKBitmap.Decode(Path.Combine(folder, file.Name));
            Assert.Equal((file.Size, file.Size), (icon.Width, icon.Height));
            // A filled icon has a solid corner; the others are see-through there.
            Assert.Equal(file.Filled ? 255 : 0, icon.GetPixel(1, 1).Alpha);
        }
        Assert.True(File.Exists(Path.Combine(folder, "favicon.svg")));
        Assert.Contains("\"short_name\": \"Karoo\"", File.ReadAllText(Path.Combine(folder, "site.webmanifest")), StringComparison.Ordinal);
        Assert.Contains("<link rel=\"icon\" href=\"/favicon.svg\" type=\"image/svg+xml\">", html, StringComparison.Ordinal);
        Assert.EndsWith("<meta name=\"theme-color\" content=\"#E8590C\">", html, StringComparison.Ordinal);

        // favicon.ico: three pictures, each a PNG, at the places its entries say.
        var ico = File.ReadAllBytes(Path.Combine(folder, "favicon.ico"));
        Assert.Equal([0, 0, 1, 0, 3, 0], ico[..6]);
        for (var i = 0; i < 3; i++)
        {
            var entry = ico.AsSpan(6 + 16 * i, 16);
            Assert.Equal(Favicons.IcoSizes[i], entry[0]);
            var length = BitConverter.ToInt32(entry[8..12]);
            var at = BitConverter.ToInt32(entry[12..16]);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], ico[at..(at + 4)]);
            using var inside = SKBitmap.Decode(ico[at..(at + length)]);
            Assert.Equal(Favicons.IcoSizes[i], inside.Width);
        }
    }

    [Fact]
    public void Without_an_svg_the_lines_leave_it_out()
    {
        Assert.DoesNotContain("svg", Favicons.Html(svg: false, "#000000"), StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Website\"", Favicons.Manifest(" ", "#000000", "#FFFFFF"), StringComparison.Ordinal);
    }
}
