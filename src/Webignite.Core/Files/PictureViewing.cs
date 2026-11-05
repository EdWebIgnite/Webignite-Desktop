using System.Globalization;

namespace Webignite.Core.Files;

/// <summary>
/// The picture viewer's rules: how big a picture is shown, the zoom steps, and the pictures beside it in its
/// folder for the arrow keys.
/// </summary>
public static class PictureViewing
{
    public const double MinZoom = 0.05;
    public const double MaxZoom = 16;

    /// <summary>A picture is decoded no bigger than this on its longest side, so a huge one can't eat the memory.</summary>
    public const int LargestSide = 4096;

    private static readonly double[] Steps = [0.05, 0.1, 0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.25, 1.5, 2, 3, 4, 6, 8, 12, 16];

    /// <summary>The zoom that fits the whole picture in the space, never bigger than its own size.</summary>
    public static double Fit(int width, int height, double spaceWidth, double spaceHeight)
    {
        if (width <= 0 || height <= 0 || spaceWidth <= 0 || spaceHeight <= 0)
            return 1;
        return Math.Clamp(Math.Min(1, Math.Min(spaceWidth / width, spaceHeight / height)), MinZoom, MaxZoom);
    }

    /// <summary>The next step up from this zoom.</summary>
    public static double In(double zoom) => Steps.FirstOrDefault(s => s > zoom + 0.001, MaxZoom);

    /// <summary>The next step down from this zoom.</summary>
    public static double Out(double zoom) => Steps.LastOrDefault(s => s < zoom - 0.001, MinZoom);

    public static string Percent(double zoom) => Math.Round(zoom * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>The pictures in a folder in name order, as the arrow keys go through them.</summary>
    public static IReadOnlyList<string> Pictures(IEnumerable<string> paths) =>
        [.. paths.Where(p => FileKinds.IsPicture(p)).OrderBy(p => System.IO.Path.GetFileName(p), NaturalOrder.Instance)];

    /// <summary>The place one step along (-1 back, +1 on), going round from the last to the first.</summary>
    public static int Step(int index, int by, int count) => count == 0 ? -1 : ((index + by) % count + count) % count;

    /// <summary>"3 of 12 · 4032 × 3024 · 2.1 MB", under the picture.</summary>
    public static string Describe(int place, int count, int width, int height, long bytes) =>
        string.Join(" · ", new[]
        {
            count > 1 ? $"{place + 1} of {count}" : null,
            width > 0 ? $"{width} × {height}" : null,
            bytes > 0 ? FileSizes.Format(bytes) : null,
        }.OfType<string>());
}
