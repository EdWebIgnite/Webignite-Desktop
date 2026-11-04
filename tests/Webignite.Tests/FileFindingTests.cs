using System.IO.Compression;
using System.Text;
using Webignite.Core.Files;

namespace Webignite.Tests;

public sealed class FileFindingTests
{
    [Theory]
    [InlineData("Bosveld website brief.pdf", "brief bosveld", true)]
    [InlineData("Café menu.docx", "cafe", true)]
    [InlineData("Quote QU-0042.pdf", "qu-0042", true)]
    [InlineData("Quote QU-0042.pdf", "invoice", false)]
    [InlineData("anything", "  ", false)]
    public void A_name_matches_when_every_word_is_in_it(string name, string query, bool matches) =>
        Assert.Equal(matches, FileSearch.NameMatches(name, query));

    [Fact]
    public void Text_files_Word_and_PDFs_can_be_looked_inside()
    {
        Assert.True(FileSearch.CanLookInside("notes.txt"));
        Assert.True(FileSearch.CanLookInside("Brief.DOCX"));
        Assert.True(FileSearch.CanLookInside("Quote.pdf"));
        Assert.False(FileSearch.CanLookInside("logo.png"));
        Assert.False(FileSearch.CanLookInside("old.doc"));
    }

    [Fact]
    public void A_snippet_shows_the_words_around_the_first_hit_on_one_line()
    {
        var text = "Dear Pieter,\n\nThe site will have six pages to start, with online booking for site visits in phase two.\nRegards";
        var snippet = FileSearch.Snippet(text, FileSearch.Words("booking"), width: 40)!;
        Assert.Contains("online booking", snippet, StringComparison.Ordinal);
        Assert.StartsWith("…", snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", snippet, StringComparison.Ordinal);
        Assert.Null(FileSearch.Snippet(text, FileSearch.Words("invoice")));
    }

    [Fact]
    public void Word_documents_are_read_as_plain_text()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), Encoding.UTF8))
            writer.Write("""<w:document><w:body><w:p><w:r><w:t>Sitemap for </w:t></w:r><w:r><w:t>Bosveld &amp; Co</w:t></w:r></w:p><w:p><w:r><w:t>Home</w:t><w:tab/><w:t>About</w:t></w:r></w:p></w:body></w:document>""");
        stream.Position = 0;
        Assert.Equal("Sitemap for Bosveld & Co\nHome About", WordText.Read(stream));
    }

    [Fact]
    public void Columns_start_at_the_client_folders_or_the_drive()
    {
        Assert.Equal([@"C:\Projects", @"C:\Projects\Bosveld", @"C:\Projects\Bosveld\Logo"],
            FileColumns.Chain(@"C:\Projects\Bosveld\Logo", @"C:\Projects"));
        Assert.Equal(["/", "/home", "/home/eddie"], FileColumns.Chain("/home/eddie", @"C:\Projects"));
        Assert.Equal([@"C:\Projects"], FileColumns.Chain(@"C:\Projects", @"C:\Projects"));
    }

    [Fact]
    public void Recents_put_the_newest_first_once()
    {
        var recents = FileRecents.Add([@"C:\a.pdf", @"C:\b.pdf"], @"c:\B.PDF");
        Assert.Equal([@"c:\B.PDF", @"C:\a.pdf"], recents);
        var full = Enumerable.Range(0, FileRecents.Most).Aggregate((IReadOnlyList<string>)[], (list, i) => FileRecents.Add(list, $"/f{i}"));
        Assert.Equal(FileRecents.Most, FileRecents.Add(full, "/new").Count);
    }

    [Fact]
    public void Quick_actions_know_what_they_can_work_on()
    {
        Assert.True(QuickActions.CanRotate("Site visit.JPG"));
        Assert.False(QuickActions.CanRotate("logo.svg"));
        Assert.True(QuickActions.CanCombine(["Quote.pdf", "photo.png"]));
        Assert.False(QuickActions.CanCombine(["Quote.pdf"]));
        Assert.False(QuickActions.CanCombine(["Quote.pdf", "notes.txt"]));
        Assert.Equal("Brief.zip", QuickActions.ZipName(["Brief.pdf"], "Bosveld"));
        Assert.Equal("Bosveld.zip", QuickActions.ZipName(["a.pdf", "b.pdf"], "Bosveld"));
    }
}
