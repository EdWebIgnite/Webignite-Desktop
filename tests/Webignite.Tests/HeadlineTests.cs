using Webignite.Core.Clients;

namespace Webignite.Tests;

/// <summary>The badges, page header lines and second row lines that make the lists say something at a glance.</summary>
public sealed class HeadlineTests
{
    private static readonly DateOnly Today = Samples.Today;

    private static StoredDocument Invoice(decimal amount, DateOnly due, DocumentStatus status = DocumentStatus.InXero,
        Settlement? settledAs = null, string? reference = null, string customer = "Karoo Coffee Roasters") => new()
    {
        Id = StoredDocument.NewId(),
        Status = status,
        SettledAs = settledAs,
        Settled = settledAs is null ? null : DateTimeOffset.UtcNow,
        Document = Samples.Completed(Samples.NewInvoice(Samples.Priced("Website", 1, amount))) with
        {
            DueOrExpiryDate = due,
            Reference = reference,
            Customer = new Customer { Name = customer },
        },
    };

    private static StoredDocument Quote(decimal amount, DateOnly expires, Settlement? settledAs = null) =>
        Invoice(amount, expires, settledAs: settledAs) with
        {
            Document = Invoice(amount, expires).Document with { Kind = DocumentKind.Quote },
            XeroStatus = "SENT",
        };

    [Theory]
    [InlineData("Karoo Coffee Roasters", "KC")]
    [InlineData("Fynbos Physio (Pty) Ltd", "FP")]
    [InlineData("The Bosveld Builders CC", "BB")]
    [InlineData("Madiba", "MA")]
    [InlineData("jan.smit@karoo.co.za", "JS")]
    [InlineData("info@karoo.co.za", "IN")]
    [InlineData("  ", "?")]
    [InlineData(null, "?")]
    [InlineData("4Seasons", "4S")]
    public void Initials_come_from_the_first_two_meaningful_words(string? name, string expected) =>
        Assert.Equal(expected, Monogram.Initials(name));

    [Fact]
    public void A_name_always_gets_the_same_badge_colour_whatever_its_case_or_spacing()
    {
        var colour = Monogram.Colour("Karoo Coffee Roasters");
        Assert.Equal(colour, Monogram.Colour("karoo coffee  roasters"));
        Assert.Contains(colour, Monogram.Colours);
        Assert.Equal("grey", Monogram.Colour(""));
    }

    [Fact]
    public void Badge_colours_are_never_red_green_yellow_or_grey()
    {
        Assert.DoesNotContain("grey", Monogram.Colours);
        Assert.DoesNotContain("red", Monogram.Colours);
        Assert.DoesNotContain("green", Monogram.Colours);
        Assert.DoesNotContain("yellow", Monogram.Colours);
    }

    [Fact]
    public void Different_clients_spread_over_the_colours()
    {
        var names = new[] { "Karoo Coffee Roasters", "Fynbos Physio", "Bosveld Builders", "Cape Dental", "Lowveld Lodge", "Umhlanga Surf", "Durban Deli", "Stellenbosch Wines" };
        Assert.True(names.Select(Monogram.Colour).Distinct().Count() >= 4);
    }

    [Fact]
    public void The_invoices_headline_counts_what_is_owed_overdue_and_still_on_this_pc()
    {
        var documents = new[]
        {
            Invoice(2800m, Today.AddDays(-5)),
            Invoice(2800m, Today.AddDays(10)),
            Invoice(7250m, Today.AddDays(-20), settledAs: Settlement.Paid),
            Invoice(2725m, Today.AddDays(30), DocumentStatus.Draft),
        };

        Assert.Equal($"4 invoices, {Money.Rand(5600m)} owed, 1 overdue, 1 not in Xero yet", DocumentHeadline.For(DocumentKind.Invoice, documents, Today));
    }

    [Fact]
    public void The_headline_leaves_out_parts_with_nothing_to_say()
    {
        Assert.Equal("1 invoice", DocumentHeadline.For(DocumentKind.Invoice, [Invoice(450m, Today, settledAs: Settlement.Paid)], Today));
        Assert.Equal("0 quotes", DocumentHeadline.For(DocumentKind.Quote, [Invoice(450m, Today)], Today));
    }

    [Fact]
    public void The_quotes_headline_says_what_is_waiting_and_what_was_accepted()
    {
        var documents = new[] { Quote(14500m, Today.AddDays(20)), Quote(3000m, Today.AddDays(5), settledAs: Settlement.Accepted) };

        Assert.Equal($"2 quotes, {Money.Rand(14500m)} waiting on clients, 1 accepted", DocumentHeadline.For(DocumentKind.Quote, documents, Today));
    }

    [Fact]
    public void A_draft_quote_the_client_never_saw_isnt_waiting_on_them()
    {
        var draft = Quote(14500m, Today.AddDays(20)) with { XeroStatus = "DRAFT" };
        Assert.Equal("1 quote", DocumentHeadline.For(DocumentKind.Quote, [draft], Today));
        Assert.Equal(0m, Receivables.QuotesOut([draft], Today));
        Assert.Equal(14500m, Receivables.QuotesOut([draft with { Emailed = DateTimeOffset.UnixEpoch }], Today));
    }

    [Fact]
    public void The_row_detail_gives_the_reference_and_the_due_date()
    {
        Assert.Equal("Website phase 2, due 17 Nov 2026", DocumentHeadline.Detail(Invoice(100m, new DateOnly(2026, 11, 17), reference: "Website phase 2"), Today));
        Assert.Equal("Due 17 Nov 2026", DocumentHeadline.Detail(Invoice(100m, new DateOnly(2026, 11, 17)), Today));
    }

    [Fact]
    public void The_row_detail_says_how_late_an_overdue_invoice_is()
    {
        Assert.Equal("Due 30 Oct 2026, yesterday", DocumentHeadline.Detail(Invoice(100m, Today.AddDays(-1)), Today));
        Assert.Equal("Due 1 Oct 2026, 30 days ago", DocumentHeadline.Detail(Invoice(100m, new DateOnly(2026, 10, 1)), Today));
    }

    [Fact]
    public void The_row_detail_says_when_it_was_paid_or_that_a_quote_expired()
    {
        var paid = Invoice(100m, Today, settledAs: Settlement.Paid) with { PaidOn = new DateOnly(2026, 10, 28) };
        Assert.Equal("Paid 28 Oct 2026", DocumentHeadline.Detail(paid, Today));
        Assert.Equal("Expired 1 Oct 2026", DocumentHeadline.Detail(Quote(100m, new DateOnly(2026, 10, 1)), Today));
        Assert.Equal("Expires 30 Nov 2026", DocumentHeadline.Detail(Quote(100m, new DateOnly(2026, 11, 30)), Today));
    }

    [Fact]
    public void Quick_filters_find_overdue_and_paid_invoices()
    {
        var overdue = Invoice(2800m, Today.AddDays(-5));
        var paid = Invoice(7250m, Today.AddDays(-20), settledAs: Settlement.Paid);
        var open = Invoice(2800m, Today.AddDays(10));
        StoredDocument[] all = [overdue, paid, open];

        Assert.Equal([overdue], new DocumentFilter(Show: DocumentShow.Overdue).Apply(all, Today));
        Assert.Equal([paid], new DocumentFilter(Show: DocumentShow.Settled).Apply(all, Today));
        Assert.True(new DocumentFilter(Show: DocumentShow.Overdue).IsActive);
    }

    [Fact]
    public void The_clients_headline_counts_clients_money_owed_and_sites_underway()
    {
        var karoo = new Client { Id = "c1", Name = "Karoo Coffee Roasters" };
        var fynbos = new Client { Id = "c2", Name = "Fynbos Physio", Onboarding = [new OnboardingItem { Name = "Logo" }] };
        var documents = new[] { Invoice(2800m, Today.AddDays(5)), Invoice(450m, Today.AddDays(5)) };
        var summaries = new[] { karoo, fynbos }.Select(c => ClientDirectory.Summarise(c, documents, [], [], Today)).ToList();

        Assert.Equal($"2 clients, {Money.Rand(3250m)} owed by 1, 1 new site underway", ClientDirectory.Headline(summaries));
    }
}

/// <summary>Who a quote or invoice was emailed to and when, on its row.</summary>
public sealed class EmailedLineTests
{
    private static readonly DateOnly Today = Samples.Today;

    private static StoredDocument Invoice(DateTimeOffset? emailed = null, string? to = null, string? reference = null) => new()
    {
        Id = StoredDocument.NewId(),
        Status = DocumentStatus.InXero,
        Emailed = emailed,
        EmailedTo = to,
        Document = Samples.Completed(Samples.NewInvoice()) with { DueOrExpiryDate = new DateOnly(2026, 11, 17), Reference = reference },
    };

    private static readonly DateTimeOffset Sent = new(2026, 11, 3, 10, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void The_row_says_who_it_went_to_and_when()
    {
        var when = Sent.ToLocalTime().ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"Website phase 2, due 17 Nov 2026, emailed to Thandi Mokoena {when}",
            DocumentHeadline.Detail(Invoice(Sent, "Thandi Mokoena", "Website phase 2"), Today));
        Assert.Equal($"Due 17 Nov 2026, emailed {when}", DocumentHeadline.Detail(Invoice(Sent), Today));
        Assert.Equal("Due 17 Nov 2026", DocumentHeadline.Detail(Invoice(), Today));
        Assert.Null(DocumentHeadline.Emailed(Invoice()));
    }
}
