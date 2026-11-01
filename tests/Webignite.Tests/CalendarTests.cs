using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Webignite.Core.Calendar;
using Webignite.Core.Tasks;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class CalendarTests : IDisposable
{
    private static readonly DateOnly Today = Samples.Today; // Saturday 31 October 2026
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;

    [Fact]
    public void Month_grid_starts_on_monday_and_ends_on_sunday()
    {
        var (first, last) = Agenda.MonthGrid(2026, 10);
        Assert.Equal(new DateOnly(2026, 9, 28), first);
        Assert.Equal(DayOfWeek.Monday, first.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 11, 1), last);
        Assert.Equal(DayOfWeek.Sunday, last.DayOfWeek);
    }

    [Fact]
    public void Week_starts_on_monday() =>
        Assert.Equal(new DateOnly(2026, 10, 26), Agenda.StartOfWeek(Today));

    [Fact]
    public void Agenda_brings_events_tasks_and_document_dates_together()
    {
        var events = new[]
        {
            new CalendarEvent { Id = "e1", Title = "Site visit", Date = Today, Start = new TimeOnly(14, 0) },
            new CalendarEvent { Id = "e2", Title = "Coffee with Sam", Date = Today, Start = new TimeOnly(9, 30) },
            new CalendarEvent { Id = "e3", Title = "Far away", Date = Today.AddDays(60) },
        };
        var tasks = new[] { new TaskItem { Id = "t1", Title = "Send sitemap", Due = Today } };
        var invoice = new StoredDocument
        {
            Id = "d1", Status = DocumentStatus.InXero, XeroNumber = "INV-0107",
            Document = Samples.Completed(Samples.NewInvoice() with { Date = Today.AddDays(-7), DueOrExpiryDate = Today }),
        };
        var draft = invoice with { Id = "d2", Status = DocumentStatus.Draft };

        var items = Agenda.Build(Today, Today.AddDays(6), events, tasks, [invoice, draft]);

        Assert.Equal(["Coffee with Sam", "Site visit", "Send sitemap", "INV-0107 due (Karoo Coffee Roasters), R 450.00"],
            items.Select(i => i.Title));
    }

    [Fact]
    public void Events_save_and_list_by_date_range()
    {
        using var store = new LocalStore(Path.Combine(folder, "c.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        store.SaveEvent(new CalendarEvent { Id = "a", Title = "In range", Date = Today });
        store.SaveEvent(new CalendarEvent { Id = "b", Title = "Out of range", Date = Today.AddDays(40) });

        Assert.Equal("In range", Assert.Single(store.ListEvents(Today.AddDays(-3), Today.AddDays(3))).Title);
        store.DeleteEvent("a");
        Assert.Empty(store.ListEvents(Today.AddDays(-3), Today.AddDays(3)));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }
}
