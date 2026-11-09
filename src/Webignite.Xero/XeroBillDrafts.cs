using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Webignite.Core.Finance;
using Webignite.Core.Xero;

namespace Webignite.Xero;

/// <summary>
/// Puts a supplier's bill into Xero as a DRAFT (adding the supplier as a contact when Eddie said to), with the
/// supplier's PDF attached. Approving and paying it stay in Xero.
/// </summary>
public sealed class XeroBillDrafts(XeroApi api)
{
    public abstract record Result
    {
        public sealed record Done(string Id, string? Problem) : Result;
        public sealed record NeedsSupplier(string Question) : Result;
    }

    public async Task<Result> DraftAsync(BillDraft bill, string accountCode, byte[]? pdf, string? pdfName, bool addSupplier, CancellationToken cancel = default)
    {
        if (BillReader.Check(bill) is { } problem)
            throw new XeroException(problem);

        var contacts = await api.ContactsAsync(cancel);
        string contactId;
        switch (XeroRules.MatchContact(bill.Supplier, null, contacts))
        {
            case ContactMatch.Use use:
                contactId = use.Contact.Id;
                break;
            case ContactMatch.New when addSupplier:
                var made = await api.PutAsync("Contacts", new JsonObject { ["Contacts"] = new JsonArray(new JsonObject { ["Name"] = bill.Supplier.Trim() }) },
                    "supplier-" + Key(bill.Supplier.Trim().ToUpperInvariant()), cancel);
                contactId = XeroApi.Str(made["Contacts"]?[0]?["ContactID"]);
                if (contactId.Length == 0)
                    throw new XeroUncertainException("Xero didn't say which contact it made. Look in Xero before trying again.");
                break;
            case ContactMatch.New:
                return new Result.NeedsSupplier($"{bill.Supplier} isn't in Xero yet. Add them as a supplier, or type the name as Xero has it.");
            case ContactMatch.Archived archived:
                return new Result.NeedsSupplier($"{archived.Contact.Name} is archived in Xero. Restore them in Xero, then try again.");
            default:
                return new Result.NeedsSupplier($"More than one contact in Xero could be {bill.Supplier}. Type the name exactly as Xero has it.");
        }

        var payload = new JsonObject
        {
            ["Type"] = "ACCPAY",
            ["Contact"] = new JsonObject { ["ContactID"] = contactId },
            ["Date"] = Day(bill.Date!.Value),
            ["Status"] = "DRAFT",
            ["LineAmountTypes"] = "NoTax",
            ["Reference"] = bill.Number ?? "",
            ["LineItems"] = new JsonArray(new JsonObject
            {
                ["Description"] = string.IsNullOrWhiteSpace(bill.Description) ? "Bill from " + bill.Supplier : bill.Description.Trim(),
                ["Quantity"] = 1m,
                ["UnitAmount"] = bill.Total!.Value,
                ["AccountCode"] = accountCode,
                ["TaxType"] = "NONE",
            }),
        };
        if (!string.IsNullOrWhiteSpace(bill.Number))
            payload["InvoiceNumber"] = bill.Number.Trim();
        if (bill.Due is { } due)
            payload["DueDate"] = Day(due);

        var key = "bill-" + Key($"{contactId}|{bill.Number}|{bill.Date:yyyyMMdd}|{bill.Total:0.00}");
        var reply = await api.PutAsync("Invoices", new JsonObject { ["Invoices"] = new JsonArray(payload) }, key, cancel);
        var id = XeroApi.Str(reply["Invoices"]?[0]?["InvoiceID"]);
        if (id.Length == 0)
            throw new XeroUncertainException("Xero's reply had no bill in it. Look in Xero before trying again.");

        string? attachProblem = null;
        if (pdf is { Length: > 0 })
        {
            try
            {
                await api.AttachAsync($"Invoices/{id}", string.IsNullOrWhiteSpace(pdfName) ? "Bill.pdf" : pdfName, pdf, "application/pdf", cancel: cancel);
            }
            catch (XeroException e)
            {
                attachProblem = "The bill is in Xero, but the PDF didn't attach: " + e.Message + " Attach it in Xero.";
            }
        }
        return new Result.Done(id, attachProblem);
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Key(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..20];
}
