using Webignite.Core.Documents;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>The editor once quotes and invoices are made in Webignite: made final with its number, locked, paid off here.</summary>
public sealed class OwnInvoicingEditorTests
{
    [Fact]
    public Task An_invoice_is_made_final_here_then_paid_off_here() => Ui.Run(async () =>
    {
        using var app = new TestApp();
        app.Services.Settings.Documents = app.Services.Settings.Documents with { MakeInWebignite = true };
        var editor = new DocumentEditorViewModel(DocumentKind.Invoice, app.Services, () => { });
        Assert.True(editor.ShowIssue);
        Assert.False(editor.ShowCreate);
        Assert.Equal("Next number", editor.NumberPlaceholder);

        editor.CustomerName = "Karoo Coffee Roasters";
        editor.Lines[0].Description = "Website hosting";
        editor.Lines[0].Price = "450";
        Assert.True(editor.CanIssue);
        await editor.IssueOnlyCommand.ExecuteAsync(null);

        Assert.True(editor.IsIssued);
        Assert.Equal("INV-0001", editor.Title);
        Assert.False(editor.CanType);
        Assert.True(editor.CanRecordPayment);

        await editor.StartPaymentCommand.ExecuteAsync(null);
        Assert.Equal("Record a payment", editor.PaymentTitle);
        editor.PaymentAmountText = "450";
        editor.PaymentNote = "EFT";
        await editor.RecordPaymentCommand.ExecuteAsync(null);

        var stored = app.Services.Documents.List(DocumentKind.Invoice).Single();
        Assert.Equal(Settlement.Paid, stored.Settled?.As);
        Assert.Equal("EFT", stored.Own!.Payments.Single().Note);
        Assert.False(editor.CanRecordPayment);
    });

    [Fact]
    public Task With_the_setting_off_it_still_goes_to_Xero() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var editor = new DocumentEditorViewModel(DocumentKind.Invoice, app.Services, () => { });
        Assert.False(editor.ShowIssue);
        Assert.True(editor.ShowCreate);
    });
}
