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
        // It shows under Money received too, linked to the invoice so the books don't count it twice.
        var money = app.Services.Store.ListEntries(DateOnly.MinValue, DateOnly.MaxValue).Single();
        Assert.Equal((450m, stored.Id), (money.Amount, money.DocumentId));
    });

    private static async Task<DocumentEditorViewModel> IssuedEditor(TestApp app, DocumentKind kind, string price = "1000")
    {
        app.Services.Settings.Documents = app.Services.Settings.Documents with { MakeInWebignite = true };
        var editor = new DocumentEditorViewModel(kind, app.Services, () => { });
        editor.CustomerName = "Bosveld Builders";
        editor.Lines[0].Description = "Website";
        editor.Lines[0].Price = price;
        await editor.IssueOnlyCommand.ExecuteAsync(null);
        return editor;
    }

    [Fact]
    public Task A_credit_note_takes_part_off_and_voiding_waits_until_nothing_is_paid() => Ui.Run(async () =>
    {
        using var app = new TestApp();
        var editor = await IssuedEditor(app, DocumentKind.Invoice);
        Assert.True(editor.CanVoid);
        Assert.Equal("Void", editor.VoidLabel);

        editor.StartCreditCommand.Execute(null);
        Assert.Equal("Make the credit note", editor.CreditButtonText);
        editor.CreditAmountText = "250";
        editor.CreditReason = "One page dropped";
        await editor.DraftCreditCommand.ExecuteAsync(null);
        var stored = app.Services.Documents.List(DocumentKind.Invoice).Single();
        Assert.Equal((750m, "CN-0001"), (OwnInvoicing.Due(stored), stored.Own!.Credits.Single().Number));
        Assert.False(editor.CanVoid);
        Assert.Contains(editor.CreditNotesText, t => t.Contains("CN-0001", StringComparison.Ordinal));
    });

    [Fact]
    public Task An_issued_invoice_with_nothing_paid_is_voided_here() => Ui.Run(async () =>
    {
        using var app = new TestApp();
        var editor = await IssuedEditor(app, DocumentKind.Invoice);
        editor.StartVoidCommand.Execute(null);
        await editor.ConfirmVoidCommand.ExecuteAsync(null);
        Assert.Equal(Settlement.Voided, app.Services.Documents.List(DocumentKind.Invoice).Single().Settled?.As);
    });

    [Fact]
    public Task A_quote_made_here_takes_the_clients_answer_here() => Ui.Run(async () =>
    {
        using var app = new TestApp();
        var editor = await IssuedEditor(app, DocumentKind.Quote, "5000");
        Assert.Equal("QU-0001", editor.Title);
        Assert.True(editor.CanDecideQuote);
        await editor.AcceptQuoteCommand.ExecuteAsync(null);
        Assert.Equal(Settlement.Accepted, app.Services.Documents.List(DocumentKind.Quote).Single().Settled?.As);
        Assert.False(editor.CanDecideQuote);
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
