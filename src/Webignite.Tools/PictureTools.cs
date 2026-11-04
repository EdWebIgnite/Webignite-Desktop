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

    /// <summary>Turns a picture a quarter to the right (90) or left (-90), saving it in place in the same format.</summary>
    public static void Rotate(string path, int degrees)
    {
        using var upright = Upright(path);
        var turned = new SKBitmap(upright.Height, upright.Width, upright.ColorType, upright.AlphaType);
        using (var canvas = new SKCanvas(turned))
        {
            canvas.Translate(degrees > 0 ? turned.Width : 0, degrees > 0 ? 0 : turned.Height);
            canvas.RotateDegrees(degrees > 0 ? 90 : -90);
            canvas.DrawBitmap(upright, 0, 0);
        }
        using (turned)
        using (var image = SKImage.FromBitmap(turned))
        {
            var format = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                ".webp" => SKEncodedImageFormat.Webp,
                ".bmp" => SKEncodedImageFormat.Bmp,
                _ => SKEncodedImageFormat.Png,
            };
            using var data = image.Encode(format, 92) ?? throw new IOException($"Couldn't save {Path.GetFileName(path)} in its format.");
            var temporary = path + ".turning";
            using (var file = File.Create(temporary))
                data.SaveTo(file);
            File.Move(temporary, path, overwrite: true);
        }
    }

    /// <summary>The picture decoded and turned the way the camera meant it.</summary>
    internal static SKBitmap Upright(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new IOException($"{Path.GetFileName(path)} isn't a picture Webignite can read.");
        using var decoded = SKBitmap.Decode(codec) ?? throw new IOException($"Couldn't read the picture in {Path.GetFileName(path)}.");
        return ImageShrinker.Upright(decoded, codec.EncodedOrigin);
    }
}
