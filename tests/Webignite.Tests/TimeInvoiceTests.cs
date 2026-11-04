using Webignite.Core.Clients;
using Webignite.Core.Time;

namespace Webignite.Tests;

/// <summary>A draft invoice from a client's logged time for a month.</summary>
public sealed class TimeInvoiceTests
{
    private static readonly Client Karoo = new()
    {
        Id = "c1", Name = "Karoo Coffee Roasters", Email = "thandi@karoocoffee.co.za", HourlyRate = 600m,
        Projects = [new() { Id = "p1", Name = "New website", HourlyRate = 750m }, new() { Id = "p2", Name = "Logo" }],
    };

    private static readonly Client NoRate = new() { Id = "c2", Name = "Fynbos Physio" };

    private static TimeEntry Entry(int minutes, DateOnly date, string client = "c1", string? project = null) =>
        new() { Id = TimeEntry.NewId(), Date = date, Minutes = minutes, ClientId = client, ProjectId = project };

    private static readonly DateOnly November = new(2026, 11, 1);

    [Fact]
    public void A_line_per_project_then_loose_time_in_hours_at_the_rate()
    {
        TimeEntry[] entries =
        [
            Entry(90, new(2026, 11, 2), project: "p1"), Entry(145, new(2026, 11, 20), project: "p1"),
            Entry(60, new(2026, 11, 3), project: "p2"),
            Entry(20, new(2026, 11, 4)),
            Entry(600, new(2026, 10, 31), project: "p1"), // October
            Entry(60, new(2026, 11, 5), client: "c2"),    // someone else
        ];
        var (lines, unpriced) = TimeInvoice.Lines(entries, Karoo, November);

        Assert.Equal(0, unpriced);
        // Hours to two decimals, and each line comes to what the minutes are worth, as on the Time page.
        Assert.Equal(
        [
            ("Logo, November 2026 (1 h)", 1.00m, 600.00m),
            ("New website, November 2026 (3 h 55 min)", 3.92m, 2937.50m),
            ("Work, November 2026 (20 min)", 0.33m, 200.00m),
        ], lines.Select(l => (l.Description, l.Quantity!.Value, l.Amount())));
        Assert.Equal(600m, lines[0].UnitPrice); // whole hours keep the plain rate
    }

    [Theory]
    [InlineData(235, 750)]
    [InlineData(20, 600)]
    [InlineData(1, 650)]
    [InlineData(6007, 875.50)]
    [InlineData(59, 1234.56)]
    public void A_line_always_comes_to_what_the_minutes_are_worth(int minutes, decimal rate)
    {
        var hours = Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);
        var price = TimeInvoice.PriceFor(hours, TimeRules.Value(minutes, rate), rate);
        Assert.Equal(TimeRules.Value(minutes, rate), new Line { Description = "x", Quantity = hours, UnitPrice = price }.Amount());
        Assert.Equal(price, Math.Round(price, 4));
    }

    [Fact]
    public void Time_with_no_rate_is_left_out_and_counted()
    {
        var (lines, unpriced) = TimeInvoice.Lines([Entry(45, new(2026, 11, 2), client: "c2")], NoRate, November);
        Assert.Empty(lines);
        Assert.Equal(45, unpriced);
        Assert.Null(TimeInvoice.Draft([Entry(45, new(2026, 11, 2), client: "c2")], NoRate, November, Samples.Settings, Samples.Today));
    }

    [Fact]
    public void The_draft_is_an_invoice_to_the_client_with_a_reference_for_the_month()
    {
        var draft = TimeInvoice.Draft([Entry(60, new(2026, 11, 2))], Karoo, November, Samples.Settings, Samples.Today);
        Assert.NotNull(draft);
        Assert.Equal(DocumentKind.Invoice, draft.Kind);
        Assert.Equal("Karoo Coffee Roasters", draft.Customer.Name);
        Assert.Equal("thandi@karoocoffee.co.za", draft.Customer.Email);
        Assert.Equal("Time, November 2026", draft.Reference);
        Assert.Equal(600m, draft.Total);
    }
}
