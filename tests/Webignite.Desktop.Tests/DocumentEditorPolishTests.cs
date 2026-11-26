using Webignite.Core.Documents;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>The quote and invoice editor's comforts: dates on a calendar, the customer from the clients, tidy prices.</summary>
public sealed class DocumentEditorPolishTests
{
    [Fact]
    public Task The_due_date_follows_the_date_until_it_is_picked_itself() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var editor = new DocumentEditorViewModel(DocumentKind.Invoice, app.Services, () => { });
        var days = app.Services.Settings.Documents.InvoiceDueDays;
        Assert.Equal(DateTime.Today, editor.DateValue);
        Assert.Equal(DateTime.Today.AddDays(days), editor.DueValue);

        editor.DateValue = new DateTime(2026, 12, 1);
        Assert.Equal(new DateTime(2026, 12, 1).AddDays(days), editor.DueValue);
        Assert.Equal(new DateOnly(2026, 12, 1), editor.Build().Date);

        editor.DueValue = new DateTime(2027, 1, 15);
        editor.DateValue = new DateTime(2026, 12, 5);
        Assert.Equal(new DateOnly(2027, 1, 15), editor.Build().DueOrExpiryDate);
    });

    [Fact]
    public Task The_customer_says_where_it_goes_and_a_new_one_can_be_saved_as_a_client() => Ui.Run(() =>
    {
        using var app = new TestApp();
        app.Services.Clients.SaveDetails(new Webignite.Core.Clients.Client { Id = "b", Name = "Bosveld Builders", Email = "pieter@bosveld.co.za" });
        var editor = new DocumentEditorViewModel(DocumentKind.Quote, app.Services, () => { });
        Assert.Contains("Bosveld Builders", editor.ClientNames);

        editor.CustomerName = "Bosveld Builders";
        Assert.Equal("Goes to pieter@bosveld.co.za", editor.CustomerHint);
        Assert.False(editor.CanSaveAsClient);

        editor.CustomerName = "Karoo Coffee Roasters";
        Assert.True(editor.CanSaveAsClient);
        editor.SaveAsClientCommand.Execute(null);
        Assert.NotNull(app.Services.Clients.Find("Karoo Coffee Roasters"));
        Assert.Contains("Karoo Coffee Roasters", editor.ClientNames);
    });

    [Fact]
    public void A_loosely_typed_price_and_quantity_are_tidied_when_the_box_is_left()
    {
        var row = new LineRowViewModel(() => { }) { Description = "Content updates", Quantity = "3,5", Price = "R650" };
        row.Settle();
        Assert.Equal(("3.5", "650.00"), (row.Quantity, row.Price));
        row.Price = "3 x 100";
        row.Settle();
        Assert.Equal("300.00", row.Price);
    }
}
