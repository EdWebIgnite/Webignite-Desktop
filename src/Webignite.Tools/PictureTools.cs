using SkiaSharp;

namespace Webignite.Tools;

/// <summary>Turning pictures and reading their size, with Skia, which the app already uses to draw.</summary>
public static class PictureTools
{
    /// <summary>The picture's size in pixels as it shows (a phone photo turned the right way), or null when it can't be read.</summary>
    public static (int Width, int Height)? Size(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null)
            return null;
        var (w, h) = (codec.Info.Width, codec.Info.Height);
        return codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom
            ? (h, w) : (w, h);
    }

    /// <summary>A PNG's pixels as BGRA (four bytes a pixel, top row first), for putting on a PDF page.</summary>
    public static (byte[] Pixels, int Width, int Height) Bgra(byte[] png)
    {
        using var decoded = SKBitmap.Decode(png) ?? throw new IOException("The picture couldn't be read.");
        using var bgra = decoded.Copy(SKColorType.Bgra8888);
        return (bgra.Bytes, bgra.Width, bgra.Height);
    }

    /// <summary>Turns a picture a quarter to the right (90) or left (-90), saving it in place in the same format.</summary>
    public static void Rotate(string path, int degrees)
    {
        using var upright = Upright(path);
        using var turned = new SKBitmap(upright.Height, upright.Width, upright.ColorType, upright.AlphaType);
        using (var canvas = new SKCanvas(turned))
        {
            canvas.Translate(degrees > 0 ? turned.Width : 0, degrees > 0 ? 0 : turned.Height);
            canvas.RotateDegrees(degrees > 0 ? 90 : -90);
            canvas.DrawBitmap(upright, 0, 0);
        }
        SaveInPlace(turned, path);
    }

    /// <summary>Flips a picture left to right (or top to bottom), saving it in place.</summary>
    public static void Flip(string path, bool topToBottom)
    {
        using var upright = Upright(path);
        using var flipped = new SKBitmap(upright.Width, upright.Height, upright.ColorType, upright.AlphaType);
        using (var canvas = new SKCanvas(flipped))
        {
            canvas.Scale(topToBottom ? 1 : -1, topToBottom ? -1 : 1, upright.Width / 2f, upright.Height / 2f);
            canvas.DrawBitmap(upright, 0, 0);
        }
        SaveInPlace(flipped, path);
    }

    /// <summary>Keeps only this part of the picture, in its own pixels (as it shows, the right way up), saved in place.</summary>
    public static void Crop(string path, int x, int y, int width, int height)
    {
        using var upright = Upright(path);
        var area = SKRectI.Intersect(new SKRectI(x, y, x + width, y + height), new SKRectI(0, 0, upright.Width, upright.Height));
        if (area.Width < 1 || area.Height < 1)
            throw new ArgumentException("Pick a part of the picture to keep.");
        using var cropped = new SKBitmap(area.Width, area.Height, upright.ColorType, upright.AlphaType);
        if (!upright.ExtractSubset(cropped, area))
            throw new IOException("Couldn't cut that part out.");
        using var copy = cropped.Copy();
        SaveInPlace(copy, path);
    }

    /// <summary>Makes the picture this many pixels across and down, saved in place.</summary>
    public static void Resize(string path, int width, int height)
    {
        using var upright = Upright(path);
        using var resized = upright.Resize(new SKImageInfo(width, height, upright.ColorType, upright.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new IOException("Couldn't resize it.");
        SaveInPlace(resized, path);
    }

    // Saved in the file's own format, through a temporary file so a failed save never leaves half a picture.
    private static void SaveInPlace(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        var format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            ".bmp" => SKEncodedImageFormat.Bmp,
            _ => SKEncodedImageFormat.Png,
        };
        using var data = image.Encode(format, 92) ?? throw new IOException($"Couldn't save {Path.GetFileName(path)} in its format.");
        var temporary = path + ".saving";
        using (var file = File.Create(temporary))
            data.SaveTo(file);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// A picture to look at: turned the way the camera meant it and shrunk so its longest side is at most
    /// <paramref name="largest"/>, as BGRA pixels. Also its full size, to show.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height, int FullWidth, int FullHeight) ForViewing(string path, int largest)
    {
        using var upright = Upright(path);
        var (fullWidth, fullHeight) = (upright.Width, upright.Height);
        var scale = Math.Min(1.0, (double)largest / Math.Max(fullWidth, fullHeight));
        var info = new SKImageInfo(Math.Max(1, (int)(fullWidth * scale)), Math.Max(1, (int)(fullHeight * scale)), SKColorType.Bgra8888, SKAlphaType.Premul);
        // Resized even at full size, so the pixels always come out as premultiplied BGRA, as the screen wants them.
        using var shown = upright.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (shown is null)
            throw new IOException($"Couldn't show the picture in {Path.GetFileName(path)}.");
        return (shown.Bytes, shown.Width, shown.Height, fullWidth, fullHeight);
    }

    /// <summary>The picture decoded and turned the way the camera meant it.</summary>
    internal static SKBitmap Upright(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new IOException($"{Path.GetFileName(path)} isn't a picture Webignite can read.");
        using var decoded = SKBitmap.Decode(codec) ?? throw new IOException($"Couldn't read the picture in {Path.GetFileName(path)}.");
        return ImageShrinker.Upright(decoded, codec.EncodedOrigin);
    }
}
