using System.Security.Cryptography;
using Webignite.Core.Files;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class FileTagTests
{
    [Fact]
    public void Tags_are_added_renamed_and_removed_keeping_their_id()
    {
        var tags = FileTags.Add(FileTags.Defaults, "Logo files");
        var added = tags[^1];
        Assert.Equal("logo-files", added.Id);
        Assert.Equal("yellow", added.Colour); // blue, orange and purple are taken
        Assert.Equal("There is already a tag with that name.", FileTags.Check(tags, "final"));
        Assert.Equal("Give the tag a name.", FileTags.Check(tags, "  "));

        tags = FileTags.Change(tags, "final", "Signed off", "teal");
        Assert.Equal(new FileTag("final", "Signed off", "teal"), tags[0]);
        Assert.Throws<ArgumentException>(() => FileTags.Change(tags, "final", "Invoice sent", "teal"));

        tags = FileTags.Remove(tags, "final");
        Assert.DoesNotContain(tags, t => t.Id == "final");
        Assert.Equal("final", FileTags.Add(tags, "Final").Last().Id);
    }

    [Fact]
    public void A_file_gets_and_loses_tags_and_goes_when_none_are_left()
    {
        var file = FileTags.With(null, @"C:\Projects\Brief.pdf", "final", true, () => "f1");
        Assert.Equal(["final"], file!.Tags);
        file = FileTags.With(file, file.Path, "final", true, () => "x");
        Assert.Equal(["final"], file!.Tags);
        file = FileTags.With(file, file.Path, "needs-ok", true, () => "x")!;
        Assert.Equal(["Final", "Needs client OK"], FileTags.Of(FileTags.Defaults, file).Select(t => t.Name));
        file = FileTags.With(file, file.Path, "final", false, () => "x")!;
        Assert.Equal("f1", file.Id);
        Assert.Null(FileTags.With(file, file.Path, "needs-ok", false, () => "x"));
    }

    [Fact]
    public void Tags_follow_a_file_or_a_whole_folder_when_moved()
    {
        TaggedFile[] files =
        [
            new() { Id = "1", Path = @"C:\Projects\Bosveld\Brief.pdf", Tags = ["final"] },
            new() { Id = "2", Path = @"C:\Projects\Bosveld\Logo\logo.svg", Tags = ["final"] },
            new() { Id = "3", Path = @"C:\Projects\Bosveld Builders\other.pdf", Tags = ["final"] },
        ];
        var moved = FileTags.Moved(files, @"c:\projects\bosveld", @"D:\Clients\Bosveld");
        Assert.Equal([@"D:\Clients\Bosveld\Brief.pdf", @"D:\Clients\Bosveld\Logo\logo.svg"], moved.Select(f => f.Path));
        Assert.Equal(@"C:\Projects\Bosveld\Brief v2.pdf", FileTags.Moved(files, files[0].Path, @"C:\Projects\Bosveld\Brief v2.pdf").Single().Path);
        Assert.True(FileTags.Same(@"C:\Projects\", @"c:\projects"));
    }

    [Fact]
    public void Group_by_tag_follows_the_tag_list_with_untagged_last()
    {
        var today = new DateOnly(2026, 11, 4);
        FileEntry File(string name) => new(@"C:\x\" + name, name, false, 1, today.ToDateTime(TimeOnly.MinValue));
        var entries = new[] { File("a.pdf"), File("b.pdf"), File("c.pdf") };
        string? FirstTag(FileEntry e) => e.Name switch { "a.pdf" => "Invoice sent", "c.pdf" => "Final", _ => null };
        var groups = FileLists.Group(entries, FileGrouping.Tag, today, FirstTag, ["Final", "Needs client OK", "Invoice sent"]);
        Assert.Equal(["Final", "Invoice sent", FileLists.NoTag], groups.Select(g => g.Heading));
    }

    [Fact]
    public void Tagged_files_are_kept_encrypted_and_read_back()
    {
        var folder = Directory.CreateTempSubdirectory("webignite-").FullName;
        try
        {
            var key = RandomNumberGenerator.GetBytes(32);
            using (var store = new LocalStore(Path.Combine(folder, "t.db"), new Vault(key)))
            {
                store.SaveTaggedFile(new TaggedFile { Id = "1", Path = @"C:\Projects\Fynbos\Quote.pdf", Tags = ["final"], FileKey = "abc-1" });
                store.SaveTaggedFile(new TaggedFile { Id = "2", Path = @"C:\Projects\Fynbos\Old.pdf", Tags = ["final"] });
                store.DeleteTaggedFile("2");
            }
            using (var again = new LocalStore(Path.Combine(folder, "t.db"), new Vault(key)))
            {
                var read = Assert.Single(again.ListTaggedFiles());
                Assert.Equal((@"C:\Projects\Fynbos\Quote.pdf", "abc-1"), (read.Path, read.FileKey));
            }
            // Windows keeps the file locked while a pooled connection holds it.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Assert.DoesNotContain("Fynbos", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder, "t.db"))), StringComparison.Ordinal);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(folder, recursive: true);
        }
    }
}
