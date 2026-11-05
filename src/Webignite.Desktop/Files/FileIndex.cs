using Webignite.Core.Files;

namespace Webignite.Desktop.Files;

/// <summary>
/// The names Ctrl+K can find files by: recent files first, then what's in the client folders, read off the
/// screen's thread each time the search opens and let go when it closes. Code and cache folders are skipped.
/// </summary>
public static class FileIndex
{
    public static Task<IReadOnlyList<string>> ReadAsync(string? root, IReadOnlyList<string> recents, CancellationToken stop = default) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var paths = new List<string>(recents.Where(p => File.Exists(p) || Directory.Exists(p)));
            if (root is null || !Directory.Exists(root))
                return paths;
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            var waiting = new Stack<(string Folder, int Depth)>();
            waiting.Push((root, 0));
            while (waiting.Count > 0 && paths.Count < FileSearch.MostIndexed && !stop.IsCancellationRequested)
            {
                var (folder, depth) = waiting.Pop();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(folder, "*", options))
                    {
                        paths.Add(file);
                        if (paths.Count >= FileSearch.MostIndexed)
                            break;
                    }
                    foreach (var sub in Directory.EnumerateDirectories(folder, "*", options))
                    {
                        if (FileSearch.SkipWhenIndexing(Path.GetFileName(sub)))
                            continue;
                        paths.Add(sub);
                        if (depth + 1 < FileSearch.IndexDepth)
                            waiting.Push((sub, depth + 1));
                    }
                }
                catch (IOException)
                {
                    // A folder that went away or can't be read is skipped; the rest still count.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            return paths;
        }, stop);
}
