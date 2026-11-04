using Avalonia.Threading;
using Webignite.Core.Files;

namespace Webignite.Desktop.Files;

/// <summary>A file found by a search, with the words around the hit when it was found inside the file.</summary>
public sealed record FileHit(FileEntry Entry, string? Snippet);

/// <summary>
/// Searches a folder and everything below it, off the screen's thread: names first, and the words inside text
/// files, Word documents and PDFs when asked. Hits come back in small batches while it runs, and it stops when
/// cancelled (a new search, another folder, or the page closing).
/// </summary>
public static class FileSearcher
{
    public static Task Run(string root, string query, bool inside, Action<IReadOnlyList<FileHit>> found, Action<int> lookedAt, CancellationToken stop) =>
        Task.Run(() =>
        {
            var words = FileSearch.Words(query);
            var batch = new List<FileHit>();
            var sent = 0;
            var looked = 0;
            var lastSend = Environment.TickCount64;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            };
            void Send(bool always)
            {
                if (!always && batch.Count < 40 && Environment.TickCount64 - lastSend < 200)
                    return;
                var hits = batch.ToList();
                var count = looked;
                batch.Clear();
                lastSend = Environment.TickCount64;
                Dispatcher.UIThread.Post(() =>
                {
                    if (stop.IsCancellationRequested)
                        return;
                    if (hits.Count > 0)
                        found(hits);
                    lookedAt(count);
                });
            }
            foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", options))
            {
                if (stop.IsCancellationRequested)
                    return;
                looked++;
                var name = Path.GetFileName(path);
                FileHit? hit = null;
                if (FileSearch.Matches(name, words))
                    hit = FileOps.Entry(path) is { } entry ? new FileHit(entry, null) : null;
                else if (inside && FileSearch.CanLookInside(name) && FileReading.Text(path) is { } text && FileSearch.Matches(text, words))
                    hit = FileOps.Entry(path) is { } entry ? new FileHit(entry, FileSearch.Snippet(text, words)) : null;
                if (hit is not null)
                {
                    batch.Add(hit);
                    if (++sent >= FileSearch.MostResults)
                        break;
                }
                Send(always: false);
            }
            Send(always: true);
        }, stop);
}
