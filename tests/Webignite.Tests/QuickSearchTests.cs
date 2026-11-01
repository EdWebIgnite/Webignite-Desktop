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
            new StoredDocument { Id = "d1", Status = DocumentStatus.InXero, XeroNumber = "INV-0107", Document = Samples.Completed(Samples.NewInvoice()) },
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
}
