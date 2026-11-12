using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>The list pages: what they show, and that searching narrows it.</summary>
public sealed class ListPageTests
{
    [Fact]
    public Task Invoices_lists_only_invoices_and_search_narrows_them() => Ui.Run(() =>
    {
        using var app = new TestApp();
        app.Add(DocumentKind.Invoice, "Karoo Coffee Roasters", "Website hosting", 450m);
        app.Add(DocumentKind.Invoice, "Fynbos Physio", "Monthly SEO", 2800m);
        app.Add(DocumentKind.Quote, "Bosveld Builders", "New website", 14500m);

        var invoices = new DocumentsViewModel(DocumentKind.Invoice, app.Services);
        Assert.Equal(2, invoices.Rows.Count);
        Assert.False(invoices.IsEmpty);
        Assert.False(invoices.NoMatches);

        invoices.Search = "fynbos";
        Assert.Equal("Fynbos Physio", Assert.Single(invoices.Rows).Customer);
        Assert.Equal($"1 of 2, {Money.Rand(2800m)}", invoices.Summary); // Rand uses spaces that don't break

        invoices.Search = "nobody by this name";
        Assert.Empty(invoices.Rows);
        Assert.True(invoices.NoMatches);

        invoices.ClearFiltersCommand.Execute(null);
        Assert.Equal(2, invoices.Rows.Count);
        Assert.Equal("", invoices.Summary);

        var quotes = new DocumentsViewModel(DocumentKind.Quote, app.Services);
        Assert.Equal("Bosveld Builders", Assert.Single(quotes.Rows).Customer);
        (invoices as IDisposable)?.Dispose();
        (quotes as IDisposable)?.Dispose();
    });

    [Fact]
    public Task A_new_install_shows_the_empty_state_not_an_empty_table() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var invoices = new DocumentsViewModel(DocumentKind.Invoice, app.Services);

        Assert.True(invoices.IsEmpty);
        Assert.False(invoices.ShowTable);
        Assert.False(invoices.NoMatches);
        (invoices as IDisposable)?.Dispose();
    });

    [Fact]
    public Task An_invoice_saved_elsewhere_shows_after_a_refresh() => Ui.Run(() =>
    {
        using var app = new TestApp();
        app.Add(DocumentKind.Invoice, "Karoo Coffee Roasters", "Website hosting", 450m);
        var invoices = new DocumentsViewModel(DocumentKind.Invoice, app.Services);
        Assert.Single(invoices.Rows);

        // As a sync or the payment check would: saved without the page knowing.
        app.Add(DocumentKind.Invoice, "Fynbos Physio", "Monthly SEO", 2800m);
        invoices.Refresh();

        Assert.Equal(2, invoices.Rows.Count);
        (invoices as IDisposable)?.Dispose();
    });

    [Fact]
    public Task Clients_shows_what_each_owes_and_offers_names_with_no_page_yet() => Ui.Run(() =>
    {
        using var app = new TestApp();
        app.Services.Clients.Add("Karoo Coffee Roasters");
        app.Add(DocumentKind.Invoice, "Karoo Coffee Roasters", "Website hosting", 450m, DocumentStatus.InXero);
        app.Add(DocumentKind.Invoice, "Fynbos Physio", "Monthly SEO", 2800m, DocumentStatus.InXero);

        var clients = new ClientsViewModel(app.Services);

        var karoo = Assert.Single(clients.Rows);
        Assert.Equal(450m, karoo.Summary.Owed);
        Assert.Equal(["Fynbos Physio"], clients.Suggestions);
        Assert.Contains("1 client", clients.Headline, StringComparison.Ordinal);

        clients.Search = "nobody";
        Assert.Empty(clients.Rows);
        (clients as IDisposable)?.Dispose();
    });
}
