using System.Text.Json.Nodes;
using Webignite.Core.Documents;
using Webignite.Core.Retainers;
using Webignite.Xero;

namespace Webignite.Tests;

/// <summary>Xero's repeating invoices brought over as retainers, paused until Xero's is stopped.</summary>
public sealed class XeroRepeatingTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static XeroRepeatingInvoice Hosting(string id = "r1") => new()
    {
        Id = id, ContactName = "Karoo Coffee Roasters", Reference = "Website hosting", Total = 450m, Unit = "MONTHLY", Period = 1,
        NextDate = new DateOnly(2026, 12, 25), Status = "AUTHORISED",
        Lines = [new Line { Description = "Website hosting", Quantity = 1, UnitPrice = 450m, SourceLineTotal = 450m }],
    };

    [Fact]
    public void A_monthly_repeating_invoice_becomes_a_paused_retainer_from_the_month_xero_bills_next()
    {
        var retainer = XeroRepeating.ToRetainer(Hosting(), Today, DateTimeOffset.UtcNow);
        Assert.Equal(("Karoo Coffee Roasters", "Website hosting", 25, new DateOnly(2026, 12, 1)),
            (retainer.ClientName, retainer.Title, retainer.DayOfMonth, retainer.Starts));
        Assert.True(retainer.Paused);
        Assert.Equal("r1", retainer.FromXeroRepeating);
        Assert.Equal(450m, retainer.MonthlyAmount);
        Assert.Null(retainer.Lines[0].SourceLineTotal);
        Assert.Contains("Stop the repeating invoice in Xero", XeroRepeating.Made(retainer), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_running_monthly_ones_not_brought_over_already_can_become_retainers()
    {
        var made = XeroRepeating.ToRetainer(Hosting(), Today, DateTimeOffset.UtcNow);
        Assert.Null(XeroRepeating.WhyNot(Hosting(), []));
        Assert.Equal("Already a retainer.", XeroRepeating.WhyNot(Hosting(), [made]));
        Assert.Equal("Not running in Xero.", XeroRepeating.WhyNot(Hosting() with { Status = "DRAFT" }, []));
        Assert.Contains("Only monthly", XeroRepeating.WhyNot(Hosting() with { Unit = "WEEKLY" }, []), StringComparison.Ordinal);
        Assert.Contains("Only monthly", XeroRepeating.WhyNot(Hosting() with { Period = 3 }, []), StringComparison.Ordinal);
        Assert.StartsWith("Monthly, next", XeroRepeating.Schedule(Hosting()), StringComparison.Ordinal);
        Assert.StartsWith("Every 3 months", XeroRepeating.Schedule(Hosting() with { Period = 3 }), StringComparison.Ordinal);
    }

    [Fact]
    public void Xeros_repeating_invoice_is_read()
    {
        var r = XeroApi.ToRepeating(JsonNode.Parse("""
            {"RepeatingInvoiceID":"r9","Type":"ACCREC","Status":"AUTHORISED","Reference":"Monthly SEO","Total":2800,
             "Contact":{"Name":"Fynbos Physio"},
             "Schedule":{"Period":1,"Unit":"MONTHLY","NextScheduledDateString":"2026-12-01T00:00:00","EndDate":"/Date(1798675200000+0000)/"},
             "LineItems":[{"Description":"SEO and Google Business updates","Quantity":1,"UnitAmount":2800,"LineAmount":2800}]}
            """)!.AsObject());
        Assert.Equal(("r9", "Fynbos Physio", "Monthly SEO", "MONTHLY", 1), (r.Id, r.ContactName, r.Reference, r.Unit, r.Period));
        Assert.Equal(new DateOnly(2026, 12, 1), r.NextDate);
        Assert.Equal(new DateOnly(2026, 12, 31), r.EndDate);
        Assert.Equal(2800m, Assert.Single(r.Lines).Amount());
    }
}
