using Webignite.Core.Files;

namespace Webignite.Tests;

public sealed class FilePowerTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static FileEntry Entry(string name, int daysAgo, bool folder = false) =>
        new(@"C:\x\" + name, name, folder, folder ? null : 10, Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(9, 0)));

    [Fact]
    public void Filters_keep_a_kind_and_an_age()
    {
        FileEntry[] entries = [Entry("Logo", 0, folder: true), Entry("photo.jpg", 3), Entry("Quote.pdf", 20), Entry("Brief.docx", 400), Entry("site.zip", 1)];
        string[] Kept(KindFilter kind, DateFilter date) => [.. entries.Where(e => FileFilters.Keeps(e, kind, date, Today)).Select(e => e.Name)];
        Assert.Equal(["photo.jpg"], Kept(KindFilter.Pictures, DateFilter.Any));
        Assert.Equal(["Brief.docx"], Kept(KindFilter.Documents, DateFilter.Any));
        Assert.Equal(["Logo", "photo.jpg", "site.zip"], Kept(KindFilter.All, DateFilter.LastWeek));
        Assert.Equal(["Brief.docx"], Kept(KindFilter.All, DateFilter.OlderThanAYear));
        Assert.Equal(["Logo"], Kept(KindFilter.Folders, DateFilter.Today));
        Assert.Equal("Pictures, last 7 days", FileFilters.Label(KindFilter.Pictures, DateFilter.LastWeek));
        Assert.Equal("Show", FileFilters.Label(KindFilter.All, DateFilter.Any));
    }

    [Fact]
    public void Space_shares_put_the_biggest_first()
    {
        var shares = FolderSpace.Shares([("a", 100), ("b", 300), ("c", 0)]);
        Assert.Equal(["b", "a", "c"], shares.Select(s => s.Path));
        Assert.Equal(0.75, shares[0].Part);
        Assert.All(FolderSpace.Shares([("a", 0)]), s => Assert.Equal(0, s.Part));
    }

    [Fact]
    public void Find_and_replace_keeps_extensions_and_ignores_case_unless_asked()
    {
        var plan = BulkRename.Plan(["IMG_001.JPG", "img_002.jpg", "notes.txt"], new() { Find = "img_", Replace = "Bosveld " }, []);
        Assert.Equal(["Bosveld 001.JPG", "Bosveld 002.jpg", "notes.txt"], plan.Select(c => c.To));
        Assert.True(BulkRename.CanApply(plan));
        var exact = BulkRename.Plan(["IMG_001.JPG", "img_002.jpg"], new() { Find = "img_", Replace = "x", MatchCase = true }, []);
        Assert.Equal(["IMG_001.JPG", "x002.jpg"], exact.Select(c => c.To));
    }

    [Fact]
    public void Numbering_pads_to_fit_the_last_number()
    {
        var names = Enumerable.Range(1, 12).Select(i => $"DSC{i}.jpg").ToList();
        var plan = BulkRename.Plan(names, new() { NumberedName = "Site visit", Digits = 1 }, []);
        Assert.Equal("Site visit 01.jpg", plan[0].To);
        Assert.Equal("Site visit 12.jpg", plan[^1].To);
        Assert.Equal("Site visit 005.jpg", BulkRename.Plan(["a.jpg"], new() { NumberedName = "Site visit", StartAt = 5, Digits = 3 }, [])[0].To);
    }

    [Fact]
    public void Clashes_and_bad_names_are_caught_before_anything_changes()
    {
        var both = BulkRename.Plan(["a 1.pdf", "a 2.pdf"], new() { Find = " 1", Replace = " 2" }, []);
        Assert.Equal("Two would get this name.", both[0].Problem);
        Assert.False(BulkRename.CanApply(both));
        var taken = BulkRename.Plan(["draft.pdf"], new() { Find = "draft", Replace = "final" }, ["final.pdf", "draft.pdf"]);
        Assert.Equal("Something else here already has this name.", taken[0].Problem);
        Assert.NotNull(BulkRename.Plan(["a.pdf"], new() { Find = "a", Replace = "b:c" }, [])[0].Problem);
        Assert.False(BulkRename.CanApply(BulkRename.Plan(["a.pdf"], new(), [])));
    }

    [Fact]
    public void Swapping_two_names_is_allowed()
    {
        var plan = BulkRename.Plan(["one.pdf", "two.pdf"], new() { NumberedName = "Page", Digits = 1 }, ["one.pdf", "two.pdf", "Page 1.pdf"]);
        Assert.Equal("Something else here already has this name.", plan[0].Problem);
        var swap = BulkRename.Plan(["Page 1.pdf", "Page 2.pdf"], new() { NumberedName = "Page", StartAt = 2, Digits = 1 }, ["Page 1.pdf", "Page 2.pdf"]);
        Assert.Equal(["Page 2.pdf", "Page 3.pdf"], swap.Select(c => c.To));
        Assert.True(BulkRename.CanApply(swap));
    }

    [Fact]
    public void Time_left_waits_for_enough_to_go_on()
    {
        Assert.Null(CopyProgress.TimeLeft(100, 1000, TimeSpan.FromSeconds(1)));
        Assert.Equal("About 2 minutes left", CopyProgress.TimeLeft(100, 1000, TimeSpan.FromSeconds(10)));
        Assert.Equal("A few seconds left", CopyProgress.TimeLeft(900, 1000, TimeSpan.FromSeconds(10)));
        Assert.Null(CopyProgress.TimeLeft(1000, 1000, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Folders_and_big_files_go_to_the_copy_queue_but_moves_on_one_drive_never_do()
    {
        Assert.True(CopyRules.Queue(anyFolder: true, 0, move: false, sameDrive: true));
        Assert.True(CopyRules.Queue(anyFolder: false, CopyRules.BigBytes + 1, move: false, sameDrive: true));
        Assert.False(CopyRules.Queue(anyFolder: false, 1000, move: false, sameDrive: true));
        Assert.False(CopyRules.Queue(anyFolder: true, CopyRules.BigBytes * 10, move: true, sameDrive: true));
        Assert.True(CopyRules.Queue(anyFolder: true, 0, move: true, sameDrive: false));
    }
}
