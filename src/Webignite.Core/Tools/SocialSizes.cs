using System.Globalization;

namespace Webignite.Core.Tools;

/// <summary>A size a social network or a link preview wants a picture in.</summary>
public sealed record SocialSize(string Network, string Name, int Width, int Height, bool Usual = false)
{
    public string Label => $"{Network} {Name}";
    public string Pixels => $"{Width.ToString(CultureInfo.InvariantCulture)} × {Height.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>Which part of the picture to keep when it has to be cropped to a different shape.</summary>
public enum SocialFocus
{
    Centre,
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>
/// One picture cut to the shapes social networks ask for. The sizes are the ones each network recommends
/// uploading at; a new one is a line in <see cref="All"/>.
/// </summary>
public static class SocialSizes
{
    public static IReadOnlyList<SocialSize> All { get; } =
    [
        new("Facebook", "post", 1200, 630, Usual: true),
        new("Facebook", "cover", 1640, 624),
        new("Instagram", "square", 1080, 1080, Usual: true),
        new("Instagram", "portrait", 1080, 1350, Usual: true),
        new("Instagram", "story or reel", 1080, 1920, Usual: true),
        new("WhatsApp", "status", 1080, 1920),
        new("LinkedIn", "post", 1200, 627, Usual: true),
        new("LinkedIn", "page cover", 1128, 191),
        new("Link", "preview (og:image)", 1200, 630, Usual: true),
        new("Google", "Business post", 1200, 900),
        new("YouTube", "thumbnail", 1280, 720),
        new("X", "post", 1600, 900),
    ];

    /// <summary>
    /// The part of the picture that is kept: as much as fits the new shape, from the middle or towards the
    /// side asked for. In the picture's own pixels.
    /// </summary>
    public static (int X, int Y, int Width, int Height) Crop(int width, int height, SocialSize size, SocialFocus focus)
    {
        var shape = (double)size.Width / size.Height;
        var (keepWidth, keepHeight) = (double)width / height > shape
            ? ((int)Math.Round(height * shape), height)
            : (width, (int)Math.Round(width / shape));
        keepWidth = Math.Clamp(keepWidth, 1, width);
        keepHeight = Math.Clamp(keepHeight, 1, height);
        var x = focus switch
        {
            SocialFocus.Left => 0,
            SocialFocus.Right => width - keepWidth,
            _ => (width - keepWidth) / 2,
        };
        var y = focus switch
        {
            SocialFocus.Top => 0,
            SocialFocus.Bottom => height - keepHeight,
            _ => (height - keepHeight) / 2,
        };
        return (x, y, keepWidth, keepHeight);
    }

    /// <summary>True when the part kept is smaller than the size wanted, so it has to be enlarged and will look soft.</summary>
    public static bool Soft(int width, int height, SocialSize size, SocialFocus focus)
    {
        var (_, _, keepWidth, keepHeight) = Crop(width, height, size, focus);
        return keepWidth < size.Width * 0.9 || keepHeight < size.Height * 0.9;
    }

    /// <summary>"karoo-opening-instagram-square-1080x1080.jpg"</summary>
    public static string FileName(string? name, SocialSize size, string format) =>
        $"{LogoPack.Stem(name) switch { "logo" => "picture", var stem => stem }}-{LogoPack.Stem(size.Label)}-{size.Width.ToString(CultureInfo.InvariantCulture)}x{size.Height.ToString(CultureInfo.InvariantCulture)}.{format}";

    /// <summary>What to say when the pictures are saved.</summary>
    public static string Saved(int made, int soft) =>
        (made == 1 ? "1 picture saved." : $"{made} pictures saved.")
        + (soft == 0 ? "" : soft == 1 ? " One had to be enlarged, so it may look soft: start from a bigger picture if you can."
            : $" {soft} had to be enlarged, so they may look soft: start from a bigger picture if you can.");
}
