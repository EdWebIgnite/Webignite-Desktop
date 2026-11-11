using System.IO.Compression;
using SkiaSharp;
using Webignite.Core.Tools;

namespace Webignite.Tools;

/// <summary>
/// A master logo, opened once and drawn at whatever sizes are asked for: an SVG (drawn sharp at any size) or a
/// picture (PNG, JPG, WebP; only ever made smaller).
/// </summary>
public sealed class LogoMaster : IDisposable
{
    private readonly Svg.Skia.SKSvg? vector;
    private readonly SKBitmap? picture;

    private LogoMaster(string path, Svg.Skia.SKSvg? vector, SKBitmap? picture, float width, float height, bool seeThrough)
    {
        Path = path;
        this.vector = vector;
        this.picture = picture;
        Width = width;
        Height = height;
        SeeThrough = seeThrough;
    }

    public string Path { get; }
    public bool IsVector => vector is not null;
    public float Width { get; }
    public float Height { get; }

    /// <summary>True when the logo has a see-through background, so all-white and all-black versions make sense.</summary>
    public bool SeeThrough { get; }

    /// <summary>Opens a logo. Throws <see cref="IOException"/> in plain words when it can't be read as one.</summary>
    public static LogoMaster Open(string path)
    {
        if (string.Equals(System.IO.Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
        {
            var svg = new Svg.Skia.SKSvg();
            SKPicture? drawn;
            try
            {
                drawn = svg.Load(path);
            }
            catch (System.Xml.XmlException)
            {
                drawn = null;
            }
            if (drawn is null || drawn.CullRect.Width < 1 || drawn.CullRect.Height < 1)
            {
                svg.Dispose();
                throw new IOException("That SVG couldn't be read, or has no size.");
            }
            // An SVG can have a background drawn in too; drawn small, its corners say whether it does.
            var asVector = new LogoMaster(path, svg, null, drawn.CullRect.Width, drawn.CullRect.Height, seeThrough: true);
            using var small = asVector.Draw(96, Math.Max(1, (int)Math.Round(96 * drawn.CullRect.Height / drawn.CullRect.Width)), LogoInk.Colour);
            return HasClearPixels(small) ? asVector : new LogoMaster(path, svg, null, drawn.CullRect.Width, drawn.CullRect.Height, seeThrough: false);
        }
        var decoded = SKBitmap.Decode(path) ?? throw new IOException("That file isn't a picture that can be read. Use a PNG, JPG, WebP or SVG.");
        return new LogoMaster(path, null, decoded, decoded.Width, decoded.Height, HasClearPixels(decoded));
    }

    // A logo on a see-through background has clear corners; a photo or a logo on white doesn't.
    private static bool HasClearPixels(SKBitmap bitmap)
    {
        if (bitmap.AlphaType == SKAlphaType.Opaque)
            return false;
        var clear = 0;
        var stepX = Math.Max(1, bitmap.Width / 64);
        var stepY = Math.Max(1, bitmap.Height / 64);
        for (var y = 0; y < bitmap.Height; y += stepY)
            for (var x = 0; x < bitmap.Width; x += stepX)
                if (bitmap.GetPixel(x, y).Alpha < 32)
                    clear++;
        return clear > 8;
    }

    /// <summary>The logo drawn to fit inside a box, in its own colours or all one, on a see-through bitmap of exactly that box.</summary>
    public SKBitmap Draw(int boxWidth, int boxHeight, LogoInk ink, double margin = 0)
    {
        var bitmap = new SKBitmap(new SKImageInfo(boxWidth, boxHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        DrawOn(canvas, new SKRect((float)(boxWidth * margin), (float)(boxHeight * margin), (float)(boxWidth * (1 - margin)), (float)(boxHeight * (1 - margin))), ink);
        return bitmap;
    }

    /// <summary>
    /// Draws the logo as big as fits in the room given, centred, on any canvas. On a PDF's canvas an SVG stays a
    /// drawing, so it prints sharp.
    /// </summary>
    public void DrawOn(SKCanvas canvas, SKRect room, LogoInk ink)
    {
        var scale = Math.Min(room.Width / Width, room.Height / Height);
        var (w, h) = (Width * scale, Height * scale);
        var into = SKRect.Create(room.MidX - w / 2, room.MidY - h / 2, w, h);
        using var paint = new SKPaint { IsAntialias = true };
        if (ink != LogoInk.Colour)
            paint.ColorFilter = SKColorFilter.CreateBlendMode(ink == LogoInk.White ? SKColors.White : SKColors.Black, SKBlendMode.SrcIn);
        if (vector?.Picture is { } drawing)
        {
            canvas.Save();
            canvas.Translate(into.Left, into.Top);
            canvas.Scale(scale);
            canvas.Translate(-drawing.CullRect.Left, -drawing.CullRect.Top);
            canvas.DrawPicture(drawing, paint);
            canvas.Restore();
        }
        else if (picture is not null)
        {
            using var image = SKImage.FromBitmap(picture);
            canvas.DrawImage(image, into, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        }
    }

    public void Dispose()
    {
        vector?.Dispose();
        picture?.Dispose();
    }
}

/// <summary>Makes the files of a logo pack and of a favicon set from a <see cref="LogoMaster"/>.</summary>
public static class LogoMaker
{
    /// <summary>One file of the pack, as its bytes.</summary>
    public static byte[] Make(LogoMaster master, LogoFile file)
    {
        var height = Math.Max(1, (int)Math.Round(file.Width * master.Height / master.Width));
        using var logo = master.Draw(file.Width, height, file.Ink);
        return Encode(logo, Colour(file.Back), file.Format);
    }

    /// <summary>A square icon: the logo in the middle, on nothing or on a colour with room round it.</summary>
    public static byte[] Icon(LogoMaster master, int size, SKColor? fill)
    {
        using var logo = master.Draw(size, size, LogoInk.Colour, fill is null ? 0 : Favicons.FilledMargin);
        return Encode(logo, fill, "png");
    }

    private static SKColor? Colour(LogoBack back) => back switch
    {
        LogoBack.White => SKColors.White,
        LogoBack.Black => SKColors.Black,
        _ => null,
    };

    private static byte[] Encode(SKBitmap logo, SKColor? behind, string format)
    {
        using var flat = new SKBitmap(new SKImageInfo(logo.Width, logo.Height, SKColorType.Rgba8888, behind is null ? SKAlphaType.Premul : SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(flat))
        {
            canvas.Clear(behind ?? SKColors.Transparent);
            canvas.DrawBitmap(logo, 0, 0);
        }
        var (kind, quality) = format switch
        {
            "jpg" => (SKEncodedImageFormat.Jpeg, 92),
            "webp" => (SKEncodedImageFormat.Webp, 90),
            _ => (SKEncodedImageFormat.Png, 100),
        };
        using var data = flat.Encode(kind, quality) ?? throw new IOException($"The {format.ToUpperInvariant()} couldn't be made.");
        return data.ToArray();
    }

    /// <summary>
    /// Writes the whole pack as one zip: every planned file, the SVG itself when the master is one, and the note
    /// on what to use where. Gives how many logo files went in.
    /// </summary>
    public static int WritePack(LogoMaster master, string? name, string zipPath)
    {
        var files = LogoPack.Plan(name, (int)master.Width, master.IsVector, master.SeeThrough);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in files)
            Put(zip, file.Path, Make(master, file));
        if (master.IsVector)
            Put(zip, $"Vector/{LogoPack.Stem(name)}.svg", File.ReadAllBytes(master.Path));
        Put(zip, "Read me.txt", System.Text.Encoding.UTF8.GetBytes(LogoPack.ReadMe(name, master.IsVector, master.SeeThrough)));
        return files.Count;
    }

    /// <summary>
    /// Writes the favicon set into a folder: the PNGs, favicon.ico, favicon.svg when the master is an SVG, and
    /// site.webmanifest. Gives the lines for the page's head.
    /// </summary>
    public static string WriteFavicons(LogoMaster master, string? name, string folder, Rgb fill, Rgb theme)
    {
        Directory.CreateDirectory(folder);
        var behind = new SKColor(fill.R, fill.G, fill.B);
        foreach (var file in Favicons.Files)
            File.WriteAllBytes(Path.Combine(folder, file.Name), Icon(master, file.Size, file.Filled ? behind : null));
        File.WriteAllBytes(Path.Combine(folder, "favicon.ico"), Favicons.Ico([.. Favicons.IcoSizes.Select(size => (size, Icon(master, size, null)))]));
        if (master.IsVector)
            File.Copy(master.Path, Path.Combine(folder, "favicon.svg"), overwrite: true);
        File.WriteAllText(Path.Combine(folder, "site.webmanifest"), Favicons.Manifest(name, theme.Hex, fill.Hex));
        var html = Favicons.Html(master.IsVector, theme.Hex);
        File.WriteAllText(Path.Combine(folder, "favicon-html.txt"), html + Environment.NewLine + Environment.NewLine + Favicons.WhereItGoes + Environment.NewLine);
        return html;
    }

    /// <summary>A small picture of the logo on a white or black tile, as BGRA pixels, for the tool to show.</summary>
    public static (byte[] Pixels, int Width, int Height) Preview(LogoMaster master, int size, LogoInk ink, bool onBlack)
    {
        using var logo = master.Draw(size, size, ink, 0.1);
        using var bgra = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bgra))
        {
            canvas.Clear(onBlack ? SKColors.Black : SKColors.White);
            canvas.DrawBitmap(logo, 0, 0);
        }
        return (bgra.Bytes, size, size);
    }

    private static void Put(ZipArchive zip, string path, byte[] bytes)
    {
        using var entry = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        entry.Write(bytes);
    }
}
