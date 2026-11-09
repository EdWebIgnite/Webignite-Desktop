using System.Text.Json.Nodes;
using Webignite.Core.Documents;
using Webignite.Core.Xero;

namespace Webignite.Xero;

/// <summary>
/// Puts an invoice right in Xero: voids it (or deletes it while it's still a draft) and drafts credit notes against
/// it. Voiding happens only when Eddie clicked and said yes; credit notes go in as DRAFT for him to approve and
/// allocate in Xero. Each change goes through <c>update</c>, applied to the document as it is stored now.
/// </summary>
public sealed class XeroCorrections(XeroApi api, Func<StoredDocument, Func<StoredDocument, StoredDocument>, StoredDocument> update)
{
    /// <summary>Voids an approved invoice without payments, or deletes a draft, after reading where it stands in Xero.</summary>
    public async Task<StoredDocument> VoidAsync(StoredDocument stored, CancellationToken cancel = default)
    {
        if (!InvoiceCorrections.CanVoid(stored))
            throw new XeroException("Only an unpaid invoice in Xero can be voided.");
        var record = await ReadAsync(stored, cancel);
        var status = XeroApi.Str(record["Status"]);
        if (status is XeroStatuses.Voided or XeroStatuses.Deleted)
            return Settle(stored, status);
        if (status == XeroStatuses.Paid || Amount(record["AmountPaid"]) > 0 || Amount(record["AmountCredited"]) > 0)
            throw new XeroException("It has a payment or credit in Xero, so Xero won't void it. Remove that in Xero first, or draft a credit note instead.");

        var wanted = XeroStatuses.IsDraft(status) ? XeroStatuses.Deleted : XeroStatuses.Voided;
        try
        {
            await api.PostAsync($"Invoices/{stored.Xero.Id}", new JsonObject { ["InvoiceID"] = stored.Xero.Id, ["Status"] = wanted },
                stored.Id + "-" + wanted.ToLowerInvariant(), cancel);
        }
        catch (XeroUncertainException)
        {
            // Reading it back says whether it landed.
        }
        if (XeroApi.Str((await ReadAsync(stored, cancel))["Status"]) != wanted)
            throw new XeroException("Couldn't tell whether Xero did it. Look at the invoice in Xero, then try again.");
        return Settle(stored, wanted);
    }

    /// <summary>
    /// Drafts a credit note in Xero against an approved invoice: for all of it when <paramref name="amount"/> is null,
    /// else for the amount, with the reason as its line. A second try after a dropped line won't make a second one.
    /// </summary>
    public async Task<StoredDocument> DraftCreditNoteAsync(StoredDocument stored, decimal? amount, string? reason, XeroTarget target, CancellationToken cancel = default)
    {
        if (!InvoiceCorrections.CanCredit(stored))
            throw new XeroException("Only an approved invoice in Xero, with something left to credit, can have a credit note.");
        if (InvoiceCorrections.CheckAmount(stored, amount) is { } problem)
            throw new XeroException(problem);
        if (InvoiceCorrections.IsFull(stored, amount))
            amount = null;
        else
            amount ??= InvoiceCorrections.Creditable(stored);

        var contactId = stored.Xero.ContactId is { Length: > 0 } known ? known : XeroApi.Str((await ReadAsync(stored, cancel))["Contact"]?["ContactID"]);
        var number = stored.Xero.Number ?? "the invoice";
        var payload = XeroPayload.CreditNote(stored.Document, number, contactId, target.AccountCode, target.TaxType,
            DateOnly.FromDateTime(DateTime.Today), amount, reason, target.BrandingThemeId);
        JsonObject made;
        try
        {
            var reply = await api.PutAsync("CreditNotes", new JsonObject { ["CreditNotes"] = new JsonArray(payload) },
                $"{stored.Id}-credit-{stored.CreditNotes.Count + 1}", cancel);
            made = reply["CreditNotes"]?[0] as JsonObject ?? throw new XeroUncertainException("Xero's reply had no credit note in it.");
        }
        catch (XeroUncertainException e)
        {
            throw new XeroException(e.Message + " Try again: Xero won't make it twice.", e);
        }
        var link = new CreditNoteLink
        {
            Id = XeroApi.Str(made["CreditNoteID"]),
            Number = XeroApi.Str(made["CreditNoteNumber"]) is { Length: > 0 } n ? n : null,
            Amount = Amount(made["Total"]) ?? amount ?? InvoiceCorrections.Creditable(stored),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            Drafted = DateTimeOffset.UtcNow,
        };
        return update(stored, s => s with { CreditNotes = [.. s.CreditNotes, link] });
    }

    private StoredDocument Settle(StoredDocument stored, string status) =>
        update(stored, s => s with
        {
            Xero = s.Xero with { Status = status, AmountDue = 0 },
            Settled = new SettledState { On = DateTimeOffset.UtcNow, As = status == XeroStatuses.Voided ? Settlement.Voided : Settlement.Deleted },
        });

    private async Task<JsonObject> ReadAsync(StoredDocument stored, CancellationToken cancel) =>
        (await api.GetAsync($"Invoices/{stored.Xero.Id}", cancel))["Invoices"]?[0] as JsonObject
        ?? throw new XeroException("Xero didn't send it back. It may have been deleted in Xero.");

    private static decimal? Amount(JsonNode? node) => node is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : null;
}
