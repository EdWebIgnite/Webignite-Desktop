using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Webignite.Core.Retainers;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class RetainerTests : IDisposable
{
    private static readonly DateOnly Today = Samples.Today; // 31 Oct 2026
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;

    private static Retainer Seo(DateOnly starts, int day = 1) => new()
    {
        Id = "r1",
        ClientName = "Fynbos Physio",
        Title = "Monthly SEO",
        DayOfMonth = day,
        Starts = starts,
        Lines = [Samples.Priced("SEO, [Month]", 1, 2800m), Samples.Priced("Hosting", 1, 450m)],
    };

    private static readonly HashSet<string> None = [];

    [Fact]
    public void Invoice_fills_in_the_month_and_totals()
    {
        var invoice = RetainerSchedule.InvoiceFor(Seo(Today), new DateOnly(2026, 11, 1), Samples.Settings);
        Assert.Equal("SEO, November 2026", invoice.Lines[0].Description);
        Assert.Equal("Monthly SEO November 2026", invoice.Reference);
        Assert.Equal(new DateOnly(2026, 11, 1), invoice.Date);
        Assert.Equal(new DateOnly(2026, 11, 8), invoice.DueOrExpiryDate);
        Assert.Equal(3250m, invoice.Total);
        Assert.Empty(DocumentValidator.Validate(invoice));
    }

    [Fact]
    public void Drafts_each_month_from_the_start_up_to_today()
    {
        var due = RetainerSchedule.Due([Seo(new DateOnly(2026, 9, 1))], None, Samples.Settings, Today);
        Assert.Equal(["retainer:r1:2026-09", "retainer:r1:2026-10"], due.Select(d => d.Key));
    }

    [Fact]
    public void Waits_for_its_day_and_skips_drafted_months()
    {
        Assert.Empty(RetainerSchedule.Due([Seo(new DateOnly(2026, 10, 1), day: 31)], None, Samples.Settings, new DateOnly(2026, 10, 30)));
        Assert.Empty(RetainerSchedule.Due([Seo(new DateOnly(2026, 10, 1))], new HashSet<string> { "retainer:r1:2026-10" }, Samples.Settings, Today));
    }

    [Fact]
    public void Next_date_moves_on_once_drafted_and_stops_when_paused()
    {
        Assert.Equal(new DateOnly(2026, 10, 1), RetainerSchedule.NextDate(Seo(new DateOnly(2026, 10, 1)), None, Today));
        Assert.Equal(new DateOnly(2026, 11, 1), RetainerSchedule.NextDate(Seo(new DateOnly(2026, 10, 1)), new HashSet<string> { "retainer:r1:2026-10" }, Today));
        Assert.Null(RetainerSchedule.NextDate(Seo(Today) with { Paused = true }, None, Today));
    }

    [Fact]
    public void Resuming_does_not_draft_the_paused_months()
    {
        var resumed = RetainerSchedule.SetPaused(Seo(new DateOnly(2026, 5, 1)) with { Paused = true }, false, Today);
        Assert.Equal(["retainer:r1:2026-10"], RetainerSchedule.Due([resumed], None, Samples.Settings, Today).Select(d => d.Key));
    }

    [Fact]
    public void Check_wants_a_client_and_priced_lines()
    {
        Assert.Empty(RetainerSchedule.Check(Seo(Today)));
        var empty = Seo(Today) with { ClientName = " ", Lines = [] };
        Assert.Equal(["Choose the client.", "Add at least one line."], RetainerSchedule.Check(empty));
        Assert.Contains("Every line needs a quantity and a price.",
            RetainerSchedule.Check(Seo(Today) with { Lines = [new Line { Description = "SEO", UnitPrice = 100m }] }));
    }

    [Fact]
    public void Store_drafts_once_and_not_again_after_a_draft_is_removed()
    {
        using var store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        store.SaveRetainer(Seo(new DateOnly(2026, 10, 1)));
        Assert.Equal(1, store.DraftRetainerInvoices(Samples.Settings, Today));
        Assert.Equal(0, store.DraftRetainerInvoices(Samples.Settings, Today));

        var draft = Assert.Single(store.List(DocumentKind.Invoice));
        Assert.Equal(DocumentStatus.Draft, draft.Status);
        Assert.Equal("Fynbos Physio", draft.Document.Customer.Name);
        Assert.Equal(draft.Id, store.DraftsMade()["retainer:r1:2026-10"]);

        store.DeleteRetainer("r1");
        Assert.Empty(store.ListRetainers());
        Assert.Single(store.List(DocumentKind.Invoice));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }
}
