using System.IO.Compression;
using Webignite.Core.Files;

namespace Webignite.Desktop.Files;

/// <summary>
/// The quick actions on right-click: turn a picture, make a web-sized copy, put files into one PDF, or zip them.
/// Each runs off the screen's thread and returns what it made. The picture and PDF libraries are read from disk
/// only when one of these is first used.
/// </summary>
public static class QuickActionRunner
{
    public static Task Rotate(IReadOnlyList<string> pictures, int degrees) => Task.Run(() => RotateNow(pictures, degrees));

    public static void RotateNow(IReadOnlyList<string> pictures, int degrees)
    {
        foreach (var picture in pictures)
            Webignite.Tools.PictureTools.Rotate(picture, degrees);
    }

    /// <summary>Web-sized copies beside the originals, as Tools' image shrinker makes them.</summary>
    public static Task<IReadOnlyList<string>> Shrink(IReadOnlyList<string> pictures) => Task.Run(() =>
    {
        var made = new List<string>();
        foreach (var picture in pictures)
        {
            var result = Webignite.Tools.ImageShrinker.Shrink(picture, new Webignite.Core.Tools.ImageJob());
            if (result.Problem is { } problem)
                throw new IOException($"{Path.GetFileName(picture)}: {problem}");
            made.Add(result.Output!);
        }
        return (IReadOnlyList<string>)made;
    });

    public static Task<string> Combine(IReadOnlyList<string> paths, string folder) => Task.Run(() =>
    {
        var output = Path.Combine(folder, FileNames.Unique("Combined.pdf", false, Names(folder)));
        Webignite.Tools.PdfMaker.Combine(paths, output);
        return output;
    });

    public static Task<string> Zip(IReadOnlyList<string> paths, string folder) => Task.Run(() =>
    {
        var name = QuickActions.ZipName([.. paths.Select(Path.GetFileName).OfType<string>()], Path.GetFileName(folder.TrimEnd('\\', '/')));
        var output = Path.Combine(folder, FileNames.Unique(name, false, Names(folder)));
        using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
        foreach (var path in paths)
        {
            var top = Path.GetFileName(path.TrimEnd('\\', '/'));
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(file, Path.Combine(top, Path.GetRelativePath(path, file)).Replace('\\', '/'), CompressionLevel.Optimal);
            }
            else
            {
                zip.CreateEntryFromFile(path, top, CompressionLevel.Optimal);
            }
        }
        return output;
    });

    private static List<string> Names(string folder) => [.. Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>()];
}
