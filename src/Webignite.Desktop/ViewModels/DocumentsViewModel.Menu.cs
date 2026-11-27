using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

// The right-click menu on a quote or invoice in the list: open it, copy it, make an invoice from a quote, open
// it in Xero, start the email, open the client, copy the number. Each opens the document first, then does what
// its own buttons do, so the same checks apply.
public partial class DocumentsViewModel
{
    /// <summary>Puts text on the clipboard; the view hands this in.</summary>
    public Func<string, Task>? CopyText { get; set; }

    private DocumentEditorViewModel? OpenRow(DocumentRow row)
    {
        Open(row);
        return Editor;
    }

    /// <summary>Deletes a quote or invoice that never reached Xero; the note that follows puts it back.</summary>
    [RelayCommand]
    private void DeleteRow(DocumentRow row) => Delete(row.Stored.Id);

    internal void Delete(string id)
    {
        if (services.Documents.Delete(id) is not { } gone)
        {
            services.ShowNote?.Invoke("It can't be deleted here", "It has gone to Xero, so it is voided or deleted from there: open it and use Void in Xero.", null);
            Refresh();
            return;
        }
        Refresh();
        var what = gone.Document.Kind == Webignite.Core.Documents.DocumentKind.Quote ? "quote" : "invoice";
        var who = string.IsNullOrWhiteSpace(gone.Document.Customer.Name) ? "" : " for " + gone.Document.Customer.Name;
        services.OfferUndo($"Deleted the {what}{who}", () =>
        {
            services.Documents.Restore(gone);
            Refresh();
        });
    }

    [RelayCommand]
    private void MakeCopyOf(DocumentRow row) => OpenRow(row)?.CopyCommand.Execute(null);

    [RelayCommand]
    private void MakeInvoiceFrom(DocumentRow row) => OpenRow(row)?.MakeInvoiceCommand.Execute(null);

    [RelayCommand]
    private Task OpenInXeroFor(DocumentRow row) => OpenRow(row)?.OpenInXeroCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand]
    private Task EmailFrom(DocumentRow row) => OpenRow(row)?.StartEmailCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand]
    private void OpenClientOf(DocumentRow row)
    {
        if (services.Clients.Find(row.CustomerName) is { } client)
            services.Go.Client(client);
    }

    [RelayCommand]
    private Task CopyNumber(DocumentRow row) =>
        OwnInvoicing.NumberOf(row.Stored) is { Length: > 0 } number && CopyText is { } copy ? copy(number) : Task.CompletedTask;
}
