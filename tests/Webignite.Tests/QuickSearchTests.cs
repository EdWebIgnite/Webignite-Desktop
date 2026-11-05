using Webignite.Core.Clients;
using Webignite.Core.Mail;
using Webignite.Core.Renewals;
using Webignite.Core.Search;
using Webignite.Core.Tasks;

namespace Webignite.Tests;

public sealed class QuickSearchTests
{
    private static readonly SearchSources Sources = new()
    {
        Clients =
        [
            new Client { Id = "c1", Name = "Karoo Coffee Roasters", Email = "hello@karoocoffee.co.za" },
            new Client { Id = "c2", Name = "Fynbos Physio", ContactPerson = "Léràto Dlamini", Notes = "Prefers WhatsApp" },
        ],
        Documents =
        [
            new StoredDocument { Id = "d1", Status = DocumentStatus.InXero, Document = Samples.Completed(Samples.NewInvoice()), Xero = new() { Number = "INV-0107" } },
        ],
        Mail =
        [
            new MailSummary { AccountId = "a", Folder = "INBOX", Uid = 1, Subject = "Booking page not loading", From = new MailPerson("Lerato Dlamini", "lerato@fynbosphysio.co.za") },
        ],
        Tasks =
        [
            new TaskItem { Id = "t1", Title = "Send Bosveld the sitemap", Created = DateTimeOffset.UnixEpoch },
            new TaskItem { Id = "t2", Title = "Old coffee task", Done = true, Created = DateTimeOffset.UnixEpoch },
        ],
        Renewals = [new Renewal { Id = "r1", Name = "karoocoffee.co.za", ClientName = "Karoo Coffee Roasters", RenewsOn = new DateOnly(2026, 11, 13) }],
    };

    [Fact]
    public void Finds_across_everything()
    {
        var kinds = QuickSearch.Find("karoo", Sources).Select(h => h.Kind).ToList();
        Assert.Contains(HitKind.Client, kinds);
        Assert.Contains(HitKind.Invoice, kinds);
        Assert.Contains(HitKind.Renewal, kinds);
    }

    [Fact]
    public void Title_starts_rank_first()
    {
        var hits = QuickSearch.Find("karoo", Sources);
        Assert.Equal(HitKind.Client, hits[0].Kind);
    }

    [Fact]
    public void Every_word_must_match()
    {
        Assert.Single(QuickSearch.Find("booking lerato", Sources));
        Assert.Empty(QuickSearch.Find("booking karoo", Sources));
    }

    [Fact]
    public void Accents_and_case_dont_matter()
    {
        var hit = QuickSearch.Find("LERATO", Sources).Single(h => h.Kind == HitKind.Client);
        Assert.Equal("Fynbos Physio", hit.Title);
    }

    [Fact]
    public void Invoice_numbers_and_notes_are_searched()
    {
        Assert.Equal(HitKind.Invoice, Assert.Single(QuickSearch.Find("inv-0107", Sources)).Kind);
        Assert.Equal(HitKind.Client, Assert.Single(QuickSearch.Find("whatsapp", Sources)).Kind);
    }

    [Fact]
    public void Done_todos_and_empty_queries_are_skipped()
    {
        Assert.Empty(QuickSearch.Find("old coffee task", Sources));
        Assert.Empty(QuickSearch.Find("   ", Sources));
    }

    private static readonly SearchSources More = Sources with
    {
        Notes =
        [
            new Webignite.Core.Notes.Note { Id = "n1", NotebookId = "b", Title = "Karoo menu ideas", Text = "seasonal blends", ClientId = "c1" },
            new Webignite.Core.Notes.Note { Id = "n2", NotebookId = "b", Title = "Karoo old plan", Deleted = DateTimeOffset.UnixEpoch },
        ],
        Logins =
        [
            new SiteLogin { Id = "l1", ClientId = "c1", Site = "WordPress admin", Address = "karoocoffee.co.za/wp-admin", UserName = "thandi", Password = "SecretKaroo1!" },
            new SiteLogin { Id = "l2", ClientId = "gone", Site = "WordPress orphan" },
        ],
        Files = [@"C:\Projects\Karoo Coffee\Logo final.ai", @"C:\Projects\Karoo Coffee\Logo final.ai", "/home/eddie/Projects/Fynbos/brief.docx"],
    };

    [Fact]
    public void Notes_are_found_by_title_and_words_but_not_from_Deleted()
    {
        var hits = QuickSearch.Find("karoo", More).Where(h => h.Kind == HitKind.Note).ToList();
        Assert.Equal("n1", Assert.Single(hits).Note!.Id);
        Assert.Contains("Karoo Coffee Roasters", hits[0].Detail, StringComparison.Ordinal);
        Assert.Equal(HitKind.Note, Assert.Single(QuickSearch.Find("seasonal", More)).Kind);
    }

    [Fact]
    public void Site_logins_are_found_by_site_and_user_but_never_by_password()
    {
        var hit = Assert.Single(QuickSearch.Find("wordpress", More));
        Assert.Equal(("l1", "Karoo Coffee Roasters"), (hit.Login!.Id, hit.LoginClient!.Name));
        Assert.Single(QuickSearch.Find("thandi", More));
        Assert.Empty(QuickSearch.Find("SecretKaroo", More));
        Assert.DoesNotContain("SecretKaroo1!", hit.Title + hit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Files_are_found_by_name_once_each_with_their_folder()
    {
        var hit = Assert.Single(QuickSearch.Find("logo final", More));
        Assert.Equal((HitKind.File, "Logo final.ai", @"C:\Projects\Karoo Coffee"), (hit.Kind, hit.Title, hit.Detail));
        Assert.Equal("brief.docx", Assert.Single(QuickSearch.Find("brief", More)).Title);
    }

    [Theory]
    [InlineData("node_modules", true)]
    [InlineData(".git", true)]
    [InlineData("wp-includes", true)]
    [InlineData("Invoices", false)]
    public void Code_and_cache_folders_are_skipped_when_reading_file_names(string folder, bool skipped) =>
        Assert.Equal(skipped, Webignite.Core.Files.FileSearch.SkipWhenIndexing(folder));
}
