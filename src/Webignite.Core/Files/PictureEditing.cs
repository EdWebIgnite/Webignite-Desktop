using Webignite.Core.Tools;

namespace Webignite.Core.Files;

/// <summary>The picture viewer's edits: the part dragged to crop, and the new size when resizing.</summary>
public static class PictureEditing
{
    /// <summary>A crop smaller than this on a side is taken as a slip of the mouse.</summary>
    public const int SmallestCrop = 4;

    /// <summary>The part dragged over, from the picture as shown at <paramref name="zoom"/>, in the picture's own pixels.</summary>
    public static ScreenGrab.Area Crop(double x1, double y1, double x2, double y2, double zoom, int width, int height)
    {
        int Px(double v) => (int)Math.Round(v / Math.Max(zoom, 0.0001));
        return ScreenGrab.Region(Px(x1), Px(y1), Px(x2), Px(y2), new ScreenGrab.Area(0, 0, width, height));
    }

    public static bool IsCrop(ScreenGrab.Area area, int width, int height) =>
        area.Width >= SmallestCrop && area.Height >= SmallestCrop && (area.Width < width || area.Height < height);

    /// <summary>
    /// The size to resize to from what was typed. With the shape kept, the side not typed follows the one typed;
    /// returns why not when it can't be used.
    /// </summary>
    public static (int Width, int Height, string? Problem) Resize(int width, int height, int? newWidth, int? newHeight, bool keepShape)
    {
        if (width <= 0 || height <= 0)
            return (0, 0, "The picture has no size.");
        if (newWidth is null && newHeight is null)
            return (0, 0, "Type the new width or height in pixels.");
        var w = newWidth ?? (int)Math.Round((double)newHeight!.Value * width / height);
        var h = newHeight ?? (int)Math.Round((double)newWidth!.Value * height / width);
        if (keepShape && newWidth is not null && newHeight is not null)
            h = (int)Math.Round((double)w * height / width);
        if (w < 1 || h < 1)
            return (0, 0, "That's too small. Make it at least 1 pixel each way.");
        if (w > 20000 || h > 20000)
            return (0, 0, "That's too big. Keep it under 20 000 pixels each way.");
        return (w, h, null);
    }

    /// <summary>The other side when one is typed and the shape is kept, for filling in the box as you type.</summary>
    public static int OtherSide(int typed, int thisSide, int otherSide) =>
        thisSide <= 0 ? 0 : Math.Max(1, (int)Math.Round((double)typed * otherSide / thisSide));
}
