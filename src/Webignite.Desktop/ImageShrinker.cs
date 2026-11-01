using SkiaSharp;
using Webignite.Core.Tools;

namespace Webignite.Desktop;

/// <summary>
/// Makes a website-sized copy of an image with Skia, which the app already uses to draw. The original is
/// never changed. Photos are turned the right way up and their camera details (including GPS) dropped.
/// </summary>
public static class ImageShrinker
{
    public sealed record Result(string Source, string? Output, long Before, long After, int Width, int Height, string? Problem);

    public static Result Shrink(string path, ImageJob job)
    {
        long before = 0;
        try
        {
            before = new FileInfo(path).Length;
            using var codec = SKCodec.Create(path);
            if (codec is null)
                return Failed("It isn't an image Webignite can read.");
            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null)
                return Failed("Couldn't read the picture in it.");
            using var upright = Upright(decoded, codec.EncodedOrigin);
            var (width, height) = ImageRules.Fit(upright.Width, upright.Height, job.MaxWidth, job.MaxHeight);
            var format = ImageRules.Output(job.Format, path);

            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888,
                format == ImageFormat.Jpeg ? SKAlphaType.Opaque : SKAlphaType.Premul));
            var canvas = surface.Canvas;
            // JPEG has no see-through parts, so those turn white rather than black.
            canvas.Clear(format == ImageFormat.Jpeg ? SKColors.White : SKColors.Transparent);
            using (var source = SKImage.FromBitmap(upright))
                canvas.DrawImage(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
            using var image = surface.Snapshot();
            using var data = image.Encode(format switch
            {
                ImageFormat.WebP => SKEncodedImageFormat.Webp,
                ImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
                _ => SKEncodedImageFormat.Png,
            }, Math.Clamp(job.Quality, 1, 100));
            if (data is null)
                return Failed("Couldn't save it in that format.");

            var output = ImageRules.OutputPath(path, format, File.Exists);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (var file = File.Create(output))
                data.SaveTo(file);
            return new Result(path, output, before, data.Size, width, height, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failed(e.Message);
        }

        Result Failed(string problem) => new(path, null, before, 0, 0, 0, problem);
    }

    // Phones store photos sideways and say which way is up; this turns the pixels to match.
    private static SKBitmap Upright(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
            return bitmap.Copy();
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        float w = swap ? bitmap.Height : bitmap.Width, h = swap ? bitmap.Width : bitmap.Height;
        var turned = new SKBitmap((int)w, (int)h, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(turned);
        canvas.SetMatrix(origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1),
            _ => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),
        });
        canvas.DrawBitmap(bitmap, 0, 0);
        return turned;
    }
}
