using System.Globalization;

namespace Webignite.Core.Tools;

public enum ImageFormat { WebP, Jpeg, Png, Same }

public sealed record ImageJob(int MaxWidth = 1920, int MaxHeight = 0, ImageFormat Format = ImageFormat.WebP, int Quality = 80);

/// <summary>How website images are shrunk: never made bigger, aspect ratio kept, saved beside the originals.</summary>
public static class ImageRules
{
    public static readonly IReadOnlyList<int> WidthChoices = [2560, 1920, 1600, 1200, 800, 400];

    public static readonly IReadOnlyCollection<string> ReadableExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };

    public static bool CanRead(string path) => ReadableExtensions.Contains(Path.GetExtension(path));

    /// <summary>The new size: fitted inside the limits (0 means no limit), never larger than the original.</summary>
    public static (int Width, int Height) Fit(int width, int height, int maxWidth, int maxHeight)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("The image has no size.");
        var scale = 1d;
        if (maxWidth > 0 && width > maxWidth)
            scale = Math.Min(scale, (double)maxWidth / width);
        if (maxHeight > 0 && height > maxHeight)
            scale = Math.Min(scale, (double)maxHeight / height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>What the file is saved as. "Same" keeps the original's kind, except GIF and BMP, which become PNG.</summary>
    public static ImageFormat Output(ImageFormat chosen, string sourcePath)
    {
        if (chosen != ImageFormat.Same)
            return chosen;
        return Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".webp" => ImageFormat.WebP,
            _ => ImageFormat.Png,
        };
    }

    public static string Extension(ImageFormat format) => format switch
    {
        ImageFormat.WebP => ".webp",
        ImageFormat.Jpeg => ".jpg",
        _ => ".png",
    };

    /// <summary>
    /// Where the new file goes: a "web" folder beside the original, with a web-friendly name
    /// ("Shop Front (1).JPG" becomes "shop-front-1.webp"). Never the original file.
    /// </summary>
    public static string OutputPath(string sourcePath, ImageFormat format, Func<string, bool> exists)
    {
        var folder = Path.Combine(Path.GetDirectoryName(sourcePath) ?? "", "web");
        var name = Slug(Path.GetFileNameWithoutExtension(sourcePath));
        var extension = Extension(format);
        var path = Path.Combine(folder, name + extension);
        for (var n = 2; exists(path); n++)
            path = Path.Combine(folder, $"{name}-{n}{extension}");
        return path;
    }

    /// <summary>Lower case, letters and digits joined by dashes, which is what website file names want.</summary>
    public static string Slug(string name)
    {
        var folded = Search.QuickSearch.Fold(name);
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in folded)
        {
            if (char.IsAsciiLetterOrDigit(c))
                current.Append(c);
            else if (current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0)
            parts.Add(current.ToString());
        return parts.Count == 0 ? "image" : string.Join('-', parts);
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024):0.0} MB"),
    };

    /// <summary>"2.4 MB to 310 KB, 87% smaller", or a note when the new file came out bigger.</summary>
    public static string Saving(long before, long after)
    {
        if (before <= 0)
            return Size(after);
        if (after >= before)
            return $"{Size(before)} to {Size(after)}, no smaller. The original was already well compressed.";
        var percent = (int)Math.Round(100d * (before - after) / before);
        return $"{Size(before)} to {Size(after)}, {percent}% smaller";
    }
}
