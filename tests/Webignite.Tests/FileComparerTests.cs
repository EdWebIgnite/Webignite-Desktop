using Webignite.Core.Files;
using Webignite.Services;

namespace Webignite.Tests;

public sealed class FileComparerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "webignite-compare-" + Guid.NewGuid().ToString("N"));

    public FileComparerTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Write(string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string WriteBytes(string relative, byte[] bytes)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Two_text_files_are_compared_line_by_line()
    {
        var result = FileComparer.Files(Write("a.css", "h1 { color: red }\r\np { margin: 0 }\r\n"), Write("b.css", "h1 { color: blue }\np { margin: 0 }\n"), ignoreSpaces: false);
        Assert.False(result.Same);
        Assert.Equal("1 line differs: 1 changed.", result.Summary);
        Assert.Equal(DiffKind.Changed, result.Rows![0].Kind);
    }

    [Fact]
    public void Text_saved_with_a_mark_at_the_start_reads_the_same_as_without()
    {
        var plain = Write("plain.txt", "Café\n");
        var marked = WriteBytes("marked.txt", [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("Café\n")]);
        Assert.True(FileComparer.Files(plain, marked, ignoreSpaces: false).Same);
    }

    [Fact]
    public void Files_that_arent_text_are_only_said_to_match_or_not()
    {
        var a = WriteBytes("a.png", [1, 0, 2, 3]);
        var same = WriteBytes("same.png", [1, 0, 2, 3]);
        var other = WriteBytes("other.png", [1, 0, 2, 4]);
        var matched = FileComparer.Files(a, same, ignoreSpaces: false);
        Assert.True(matched.Same);
        Assert.Null(matched.Rows);
        Assert.Equal("The two files are exactly the same.", matched.Summary);
        var differed = FileComparer.Files(a, other, ignoreSpaces: false);
        Assert.False(differed.Same);
        Assert.StartsWith("The two files are different, though the same size.", differed.Summary);
    }

    [Fact]
    public void Two_folders_are_compared_by_what_is_in_their_files()
    {
        Write("left/index.html", "<h1>Home</h1>");
        Write("left/css/site.css", "body { }");
        Write("left/old.txt", "old");
        Write("right/index.html", "<h1>Home</h1>");
        Write("right/css/site.css", "body {}");
        Write("right/new.txt", "new");
        // Saved at another time but holding the same words: still the same.
        File.SetLastWriteTimeUtc(Path.Combine(root, "right", "index.html"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var (rows, summary) = FileComparer.Folders(Path.Combine(root, "left"), Path.Combine(root, "right"), TestContext.Current.CancellationToken);

        var by = rows!.ToDictionary(r => r.Path.Replace('\\', '/'), r => r.Kind);
        Assert.Equal(FolderDiff.Same, by["index.html"]);
        Assert.Equal(FolderDiff.Different, by["css/site.css"]);
        Assert.Equal(FolderDiff.OnlyLeft, by["old.txt"]);
        Assert.Equal(FolderDiff.OnlyRight, by["new.txt"]);
        Assert.Equal("3 of 4 files differ: 1 different, 1 only on the left, 1 only on the right.", summary);
    }

    [Fact]
    public void Copies_are_found_wherever_they_sit_and_whatever_they_are_called()
    {
        var big = new byte[200_000];
        new Random(3).NextBytes(big);
        var nearly = (byte[])big.Clone();
        nearly[^1] ^= 1; // the same until the very last byte
        WriteBytes("logo.psd", big);
        WriteBytes("Copy of work/logo final.psd", big);
        WriteBytes("nearly.psd", nearly);
        Write("notes.txt", "hello");
        Write("backup/notes copy.txt", "hello");
        Write("other.txt", "HELLO");
        Write("empty1.txt", "");
        Write("empty2.txt", "");

        var progress = new List<DuplicateProgress>();
        var sets = FileComparer.Duplicates(root, new Told(progress), TestContext.Current.CancellationToken);

        Assert.Equal(2, sets.Count);
        Assert.Equal(["Copy of work/logo final.psd", "logo.psd"], sets[0].Paths.Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')));
        Assert.Equal(200_000, sets[0].Spare);
        Assert.Equal(["backup/notes copy.txt", "notes.txt"], sets[1].Paths.Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')));
        Assert.Equal([Path.Combine(root, "Copy of work", "logo final.psd")], Duplicates.Extras(sets[0]));
        Assert.NotEmpty(progress);
    }

    [Fact]
    public void A_search_can_be_stopped()
    {
        Write("a.txt", "same");
        Write("b.txt", "same");
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => FileComparer.Duplicates(root, null, stop.Token));
    }

    private sealed class Told(List<DuplicateProgress> seen) : IProgress<DuplicateProgress>
    {
        public void Report(DuplicateProgress value) => seen.Add(value);
    }
}
