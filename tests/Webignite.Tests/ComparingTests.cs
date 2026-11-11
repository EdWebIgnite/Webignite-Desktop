using Webignite.Core.Files;

namespace Webignite.Tests;

public class ComparingTests
{
    private static string Kinds(IEnumerable<DiffRow> rows) => string.Concat(rows.Select(r => r.Kind switch
    {
        DiffKind.Same => '=', DiffKind.Removed => '-', DiffKind.Added => '+', DiffKind.Changed => '~', _ => '.',
    }));

    [Fact]
    public void Lines_are_split_whatever_the_line_endings()
    {
        Assert.Equal(["a", "b", "c"], TextCompare.Lines("a\r\nb\nc\r"));
        Assert.Equal(["a", "", "b"], TextCompare.Lines("a\n\nb\n"));
        Assert.Empty(TextCompare.Lines(""));
    }

    [Fact]
    public void Two_files_the_same_are_all_matching_rows()
    {
        var rows = TextCompare.Rows(["a", "b"], ["a", "b"]);
        Assert.Equal("==", Kinds(rows));
        Assert.Equal("The two files are the same.", TextCompare.Summary(rows));
    }

    [Fact]
    public void A_changed_line_sits_beside_what_it_became()
    {
        var rows = TextCompare.Rows(["h1 { color: red }", "p { margin: 0 }", "a { }"], ["h1 { color: blue }", "p { margin: 0 }", "a { }"]);
        Assert.Equal("~==", Kinds(rows));
        Assert.Equal((1, "h1 { color: red }", 1, "h1 { color: blue }"), (rows[0].LeftNumber, rows[0].Left, rows[0].RightNumber, rows[0].Right));
        Assert.Equal("1 line differs: 1 changed.", TextCompare.Summary(rows));
    }

    [Fact]
    public void Lines_only_one_side_has_get_rows_of_their_own_and_numbering_carries_on()
    {
        var rows = TextCompare.Rows(["a", "b", "c", "d"], ["a", "c", "x", "y", "d"]);
        Assert.Equal("=-=++=", Kinds(rows));
        Assert.Equal((2, "b", null, null), (rows[1].LeftNumber, rows[1].Left, rows[1].RightNumber, rows[1].Right));
        Assert.Equal((null, 3, "x"), (rows[3].LeftNumber, rows[3].RightNumber, rows[3].Right));
        Assert.Equal((4, 5), (rows[5].LeftNumber, rows[5].RightNumber));
        Assert.Equal("3 lines differ: 1 only on the left, 2 only on the right.", TextCompare.Summary(rows));
    }

    [Fact]
    public void An_empty_file_against_a_full_one_is_all_one_sided()
    {
        Assert.Equal("++", Kinds(TextCompare.Rows([], ["a", "b"])));
        Assert.Equal("--", Kinds(TextCompare.Rows(["a", "b"], [])));
        Assert.Empty(TextCompare.Rows([], []));
    }

    [Fact]
    public void Spaces_at_the_ends_of_lines_can_be_ignored()
    {
        Assert.Equal("~", Kinds(TextCompare.Rows(["  a\t"], ["a"])));
        Assert.Equal("=", Kinds(TextCompare.Rows(["  a\t"], ["a"], ignoreSpaces: true)));
    }

    [Fact]
    public void Whatever_the_files_the_rows_rebuild_both_of_them()
    {
        // The comparison must never lose or invent a line: reading down each side gives each file back.
        var random = new Random(7);
        for (var round = 0; round < 300; round++)
        {
            var left = Enumerable.Range(0, random.Next(0, 40)).Select(_ => "line " + random.Next(0, 8)).ToList();
            var right = Enumerable.Range(0, random.Next(0, 40)).Select(_ => "line " + random.Next(0, 8)).ToList();
            var rows = TextCompare.Rows(left, right);
            Assert.Equal(left, rows.Where(r => r.Left is not null).Select(r => r.Left!));
            Assert.Equal(right, rows.Where(r => r.Right is not null).Select(r => r.Right!));
            Assert.Equal(Enumerable.Range(1, left.Count), rows.Where(r => r.LeftNumber is not null).Select(r => r.LeftNumber!.Value));
            Assert.Equal(Enumerable.Range(1, right.Count), rows.Where(r => r.RightNumber is not null).Select(r => r.RightNumber!.Value));
            Assert.All(rows.Where(r => r.Kind == DiffKind.Same), r => Assert.Equal(r.Left, r.Right));
            Assert.All(rows.Where(r => r.Kind == DiffKind.Changed), r => Assert.NotEqual(r.Left, r.Right));
        }
    }

    [Fact]
    public void A_small_change_in_a_long_file_is_found_as_small()
    {
        var left = Enumerable.Range(0, 5000).Select(i => "row " + i).ToList();
        var right = left.ToList();
        right[2500] = "changed";
        right.RemoveAt(100);
        var rows = TextCompare.Rows(left, right);
        Assert.Equal("2 lines differ: 1 changed, 1 only on the left.", TextCompare.Summary(rows));
    }

    [Fact]
    public void Two_long_files_with_nothing_in_common_still_get_an_answer()
    {
        var left = Enumerable.Range(0, 3000).Select(i => "left " + i).ToList();
        var right = Enumerable.Range(0, 3000).Select(i => "right " + i).ToList();
        var rows = TextCompare.Rows(left, right);
        Assert.Equal(3000, rows.Count(r => r.Kind == DiffKind.Changed));
    }

    [Fact]
    public void Only_the_differences_keeps_a_few_lines_round_each_and_folds_the_rest()
    {
        var left = Enumerable.Range(1, 30).Select(i => "row " + i).ToList();
        var right = left.ToList();
        right[14] = "changed";
        var shown = TextCompare.OnlyChanges(TextCompare.Rows(left, right), around: 2);
        Assert.Equal(".==~==.", Kinds(shown));
        Assert.Equal("12 lines the same", shown[0].Left);
        Assert.Equal("13 lines the same", shown[^1].Left);
        // With nothing different, everything folds into one row.
        Assert.Equal(".", Kinds(TextCompare.OnlyChanges(TextCompare.Rows(left, left))));
    }

    [Fact]
    public void A_file_with_zero_bytes_in_it_isnt_text()
    {
        Assert.True(TextCompare.LooksLikeText("body { }"u8));
        Assert.False(TextCompare.LooksLikeText([0x89, 0x50, 0x4E, 0x47, 0, 0]));
    }

    [Fact]
    public void Folders_are_compared_file_by_file()
    {
        var day = new DateTime(2026, 11, 1, 9, 0, 0);
        FolderItem[] left = [new("index.html", 100, day), new("css/site.css", 50, day), new("old.txt", 5, day), new("logo.png", 900, day), new("same-size.js", 70, day)];
        FolderItem[] right = [new("INDEX.html", 100, day), new("css/site.css", 60, day), new("new.txt", 5, day), new("logo.png", 900, day.AddDays(1)), new("same-size.js", 70, day.AddDays(1))];
        var read = new List<string>();
        var rows = FolderCompare.Rows(left, right, path =>
        {
            read.Add(path);
            return path == "logo.png";
        });

        var by = rows.ToDictionary(r => r.Path, r => r.Kind, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(FolderDiff.Same, by["index.html"]);
        Assert.Equal(FolderDiff.Different, by["css/site.css"]);
        Assert.Equal(FolderDiff.OnlyLeft, by["old.txt"]);
        Assert.Equal(FolderDiff.OnlyRight, by["new.txt"]);
        Assert.Equal(FolderDiff.Same, by["logo.png"]);
        Assert.Equal(FolderDiff.Different, by["same-size.js"]);
        // Only files that could be either way are read: same size, different time.
        Assert.Equal(["logo.png", "same-size.js"], read);
        Assert.Equal("4 of 6 files differ: 2 different, 1 only on the left, 1 only on the right.", FolderCompare.Summary(rows));
    }

    [Fact]
    public void Folders_that_match_say_so()
    {
        var day = new DateTime(2026, 11, 1);
        FolderItem[] files = [new("a.txt", 1, day), new("b.txt", 2, day)];
        Assert.Equal("The two folders hold the same 2 files.", FolderCompare.Summary(FolderCompare.Rows(files, files, _ => true)));
        Assert.Equal("Both folders are empty.", FolderCompare.Summary(FolderCompare.Rows([], [], _ => true)));
    }

    [Fact]
    public void Only_files_that_share_a_size_can_be_copies()
    {
        var sets = Duplicates.SameSize([("a.jpg", 500), ("b.jpg", 500), ("c.jpg", 700), ("empty1.txt", 0), ("empty2.txt", 0), ("d.jpg", 500)]);
        Assert.Equal(["a.jpg", "b.jpg", "d.jpg"], Assert.Single(sets));
    }

    [Fact]
    public void Files_of_one_size_are_copies_only_when_their_content_matches()
    {
        var prints = new Dictionary<string, string?> { ["a.jpg"] = "X", ["b.jpg"] = "Y", ["d.jpg"] = "X", ["locked.jpg"] = null, ["e.jpg"] = "Z" };
        var sets = Duplicates.SameFingerprint(["a.jpg", "b.jpg", "d.jpg", "locked.jpg", "e.jpg"], path => prints[path]);
        Assert.Equal(["a.jpg", "d.jpg"], Assert.Single(sets));
    }

    [Fact]
    public void The_copy_kept_is_the_one_nearest_the_top_of_the_folder()
    {
        var set = new DuplicateSet(1000, ["C:/Work/Copy of logos/logo.png", "C:/Work/logo.png", "C:/Work/logo (2).png"]);
        Assert.Equal(["C:/Work/Copy of logos/logo.png", "C:/Work/logo (2).png"], Duplicates.Extras(set));
        Assert.Equal(2000, set.Spare);
    }

    [Fact]
    public void Ticking_every_copy_still_leaves_one()
    {
        var set = new DuplicateSet(1000, ["C:/Work/Copies/logo.png", "C:/Work/logo.png"]);
        Assert.Equal(["C:/Work/logo.png"], Duplicates.SafeToRemove(set, ["C:/Work/logo.png"]));
        Assert.Equal(["C:/Work/Copies/logo.png"], Duplicates.SafeToRemove(set, ["C:/Work/Copies/logo.png", "C:/Work/logo.png"]));
        Assert.Empty(Duplicates.SafeToRemove(set, []));
    }

    [Fact]
    public void Sets_that_free_the_most_come_first_and_the_summary_adds_them_up()
    {
        var small = new DuplicateSet(1024, ["b.txt", "a.txt"]);
        var big = new DuplicateSet(5 * 1024 * 1024, ["z.psd", "y.psd", "x.psd"]);
        var ordered = Duplicates.Ordered([small, big]);
        Assert.Equal(["x.psd", "y.psd", "z.psd"], ordered[0].Paths);
        Assert.Equal(["a.txt", "b.txt"], ordered[1].Paths);
        Assert.StartsWith("3 files are copies of others, in 2 sets. Removing the extra copies would free 10", Duplicates.Summary(ordered));
        Assert.StartsWith("1 file is a copy of another. Removing the extra copy would free", Duplicates.Summary([small]));
        Assert.Equal("No two files in there are the same.", Duplicates.Summary([]));
    }
}
