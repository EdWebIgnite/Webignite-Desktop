using SkiaSharp;
using Webignite.Core.Tools;

namespace Webignite.Tools;

/// <summary>Cuts one picture to the shapes social networks ask for. The picture itself is never changed.</summary>
public static class SocialImages
{
    /// <summary>The picture's size, turned the way the camera meant it.</summary>
    public static (int Width, int Height) Size(string path)
    {
        using var picture = PictureTools.Upright(path);
        return (picture.Width, picture.Height);
    }

    /// <summary>The picture cut and sized for one network, as a JPG or PNG.</summary>
    public static byte[] Make(string path, SocialSize size, SocialFocus focus, string format)
    {
        using var picture = PictureTools.Upright(path);
        using var cut = Cut(picture, size, focus, size.Width, size.Height);
        using var data = cut.Encode(format == "png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 90)
            ?? throw new IOException("The picture couldn't be saved in that format.");
        return data.ToArray();
    }

    /// <summary>
    /// Every size wanted, saved into a folder, named after what each is for. The picture is read once. Gives how
    /// many were made and how many had to be enlarged.
    /// </summary>
    public static (int Made, int Soft, string Last) Save(string path, string? name, IReadOnlyList<SocialSize> sizes, SocialFocus focus, string format, string folder)
    {
        using var picture = PictureTools.Upright(path);
        Directory.CreateDirectory(folder);
        var (made, soft, last) = (0, 0, "");
        foreach (var size in sizes)
        {
            using var cut = Cut(picture, size, focus, size.Width, size.Height);
            using var data = cut.Encode(format == "png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 90)
                ?? throw new IOException("The picture couldn't be saved in that format.");
            last = Path.Combine(folder, SocialSizes.FileName(name, size, format));
            File.WriteAllBytes(last, data.ToArray());
            made++;
            if (SocialSizes.Soft(picture.Width, picture.Height, size, focus))
                soft++;
        }
        return (made, soft, last);
    }

    /// <summary>Small versions of each cut, as BGRA pixels, to show before saving. The longest side is <paramref name="longest"/>.</summary>
    public static IReadOnlyList<(SocialSize Size, byte[] Pixels, int Width, int Height, bool Soft)> Previews(string path, IReadOnlyList<SocialSize> sizes, SocialFocus focus, int longest)
    {
        using var picture = PictureTools.Upright(path);
        var shown = new List<(SocialSize, byte[], int, int, bool)>();
        foreach (var size in sizes)
        {
            var scale = (double)longest / Math.Max(size.Width, size.Height);
            var (width, height) = (Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
            using var cut = Cut(picture, size, focus, width, height);
            shown.Add((size, cut.Bytes, width, height, SocialSizes.Soft(picture.Width, picture.Height, size, focus)));
        }
        return shown;
    }

    // The kept part of the picture, drawn at the size wanted, on white in case the picture is see-through.
    private static SKBitmap Cut(SKBitmap picture, SocialSize size, SocialFocus focus, int width, int height)
    {
        var (x, y, keepWidth, keepHeight) = SocialSizes.Crop(picture.Width, picture.Height, size, focus);
        var cut = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(cut);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(picture);
        canvas.DrawImage(image, SKRect.Create(x, y, keepWidth, keepHeight), SKRect.Create(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return cut;
    }
}
