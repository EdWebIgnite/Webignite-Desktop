using Webignite.Core.Clients;
using Webignite.Core.Files;

namespace Webignite.Tests;

public sealed class FileRuleTests
{
    private static FileEntry File(string name, long size = 10, int daysAgo = 0) =>
        new("/p/" + name, name, false, size, new DateTime(2026, 11, 4, 9, 0, 0).AddDays(-daysAgo));

    private static FileEntry Folder(string name) => new("/p/" + name, name, true, null, new DateTime(2026, 11, 1));

    [Theory]
    [InlineData("Brief.PDF", false, "PDF")]
    [InlineData("logo.png", false, "Picture")]
    [InlineData("Quote.docx", false, "Word document")]
    [InlineData("backup.xyz", false, "XYZ file")]
    [InlineData("README", false, "File")]
    [InlineData("Karoo", true, "Folder")]
    public void Kinds_are_named_the_way_people_say_them(string name, bool folder, string kind) => Assert.Equal(kind, FileKinds.Of(name, folder));

    [Fact]
    public void Folders_come_first_and_numbers_sort_as_numbers()
    {
        var sorted = FileLists.Sort([File("Page 10.png"), Folder("Zulu"), File("Page 2.png"), Folder("alpha")], FileSort.Name, descending: false);
        Assert.Equal(["alpha", "Zulu", "Page 2.png", "Page 10.png"], sorted.Select(e => e.Name));
    }

    [Fact]
    public void Biggest_first_keeps_folders_on_top()
    {
        var sorted = FileLists.Sort([File("small", 1), Folder("A"), File("big", 900)], FileSort.Size, descending: true);
        Assert.Equal(["A", "big", "small"], sorted.Select(e => e.Name));
    }

    [Fact]
    public void Grouped_by_date_newest_first_and_by_kind_with_folders_on_top()
    {
        var today = new DateOnly(2026, 11, 4); // a Wednesday
        IReadOnlyList<FileEntry> entries = [File("a", daysAgo: 0), File("b", daysAgo: 1), File("c", daysAgo: 20), File("d", daysAgo: 400)];
        var byDate = FileLists.Group(entries, FileGrouping.Date, today);
        Assert.Equal(["Today", "Yesterday", "Earlier this year", "Older"], byDate.Select(g => g.Heading));
        var byKind = FileLists.Group([Folder("x"), File("a.pdf"), File("b.png")], FileGrouping.Kind, today);
        Assert.Equal(["Folders", "PDF", "Picture"], byKind.Select(g => g.Heading));
    }

    [Theory]
    [InlineData("Final brief.pdf", null)]
    [InlineData("", "Type a name.")]
    [InlineData("a/b", "A name can't have any of these in it: \\ / : * ? \" < > |")]
    [InlineData("notes.", "A name can't end with a full stop.")]
    [InlineData("CON.txt", "Windows keeps the name CON.txt for itself.")]
    public void Names_windows_wont_take_are_refused(string name, string? problem) => Assert.Equal(problem, FileNames.Problem(name));

    [Fact]
    public void New_folders_and_copies_get_free_names()
    {
        Assert.Equal("New folder", FileNames.NewFolder(["Brief.pdf"]));
        Assert.Equal("New folder (3)", FileNames.NewFolder(["New folder", "new folder (2)"]));
        Assert.Equal("Brief - Copy.pdf", FileNames.CopyOf("Brief.pdf", false, ["Brief.pdf"]));
        Assert.Equal("Brief - Copy (2).pdf", FileNames.CopyOf("Brief.pdf", false, ["Brief.pdf", "Brief - Copy.pdf"]));
        Assert.Equal("Logo (2).png", FileNames.Unique("Logo.png", false, ["logo.png"]));
        Assert.Equal("Fresh.png", FileNames.Unique("Fresh.png", false, ["logo.png"]));
    }

    [Fact]
    public void The_address_bar_shows_each_folder_on_the_way()
    {
        Assert.Equal([("C:", "C:\\"), ("Projects", @"C:\Projects"), ("Karoo", @"C:\Projects\Karoo")], FilePaths.Crumbs(@"C:\Projects\Karoo"));
        Assert.Equal(@"C:\", FilePaths.Parent(@"C:\Projects"));
        Assert.Null(FilePaths.Parent(@"C:\"));
        Assert.Equal("/", FilePaths.Parent("/home"));
        Assert.Equal("/home", FilePaths.Parent("/home/eddie/"));
    }

    [Theory]
    [InlineData(500L, "500 B")]
    [InlineData(1500L, "2 KB")]
    [InlineData(2_500_000L, "2.4 MB")]
    [InlineData(null, "")]
    public void Sizes_read_easily(long? bytes, string shown) =>
        Assert.Equal(shown, FileSizes.Format(bytes).Replace(',', '.'));

    [Fact]
    public void A_client_finds_its_folder_by_name_and_a_near_miss_is_asked_about()
    {
        string[] folders = [@"C:\Projects\Karoo Coffee", @"C:\Projects\Bosveld Builders", @"C:\Projects\Fynbos"];
        Assert.Equal(new ClientFolders.Match(ClientFolders.Fit.Sure, @"C:\Projects\Bosveld Builders"),
            ClientFolders.For(new Client { Id = "1", Name = "Bosveld Builders (Pty) Ltd" }, folders));
        Assert.Equal(new ClientFolders.Match(ClientFolders.Fit.Likely, @"C:\Projects\Karoo Coffee"),
            ClientFolders.For(new Client { Id = "2", Name = "Karoo Coffee Roasters" }, folders));
        Assert.Equal(ClientFolders.Fit.None, ClientFolders.For(new Client { Id = "3", Name = "Lerato Designs" }, folders).Fit);
        Assert.Equal(new ClientFolders.Match(ClientFolders.Fit.Sure, @"D:\Elsewhere"),
            ClientFolders.For(new Client { Id = "4", Name = "Fynbos Physio", Folder = @"D:\Elsewhere" }, folders));
    }

    [Theory]
    [InlineData("Smith & Co. (Pty) Ltd.", "Smith & Co. (Pty) Ltd")]
    [InlineData("Plumb/Fix: Durban", "Plumb Fix Durban")]
    [InlineData("  ", "Client")]
    [InlineData("CON", "Client")]
    public void A_client_name_becomes_a_folder_name_Windows_takes(string name, string folder) =>
        Assert.Equal(folder, FileNames.Safe(name));
}
