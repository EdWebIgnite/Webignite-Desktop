using System.Globalization;
using System.Text.Json.Nodes;
using Webignite.Core.Documents;
using Webignite.Core.Xero;

namespace Webignite.Xero;

/// <summary>
/// Gets a checked Xero draft ready to email from Webignite's own mailbox: approves invoices (only when a
/// person clicked to), downloads Xero's PDF and online link, and afterwards tells Xero it was sent.
/// Nothing here sends mail; that stays with the Send button.
/// </summary>
public sealed class XeroSending(XeroApi api, Func<StoredDocument, StoredDocument> save)
{
    /// <summary>Everything the email needs, read from Xero so it matches the PDF.</summary>
    public sealed record Ready(StoredDocument Stored, byte[] Pdf, string PdfName, DocumentEmailValues Values, string? ContactEmail);

    private static bool IsQuote(StoredDocument s) => s.Document.Kind == DocumentKind.Quote;
    private static string Resource(StoredDocument s) => IsQuote(s) ? "Quotes" : "Invoices";

    /// <summary>Whether it can be emailed yet, or why not.</summary>
    public static string? CannotSend(StoredDocument stored) =>
        stored.XeroId is null || stored.XeroState is not (XeroWriteState.Verified or XeroWriteState.Mismatch)
            ? "Create it in Xero first, and let Webignite check it."
            : null;

    /// <summary>
    /// Approves an invoice in Xero (a quote stays as it is until it's sent) and gathers the PDF, online link
    /// and amounts. Approving is one try only; if the line drops, Xero is read again to see where it stands.
    /// </summary>
    public async Task<Ready> PrepareAsync(StoredDocument stored, string tradingName, CancellationToken cancel = default)
    {
        if (CannotSend(stored) is { } reason)
            throw new XeroException(reason);
        var record = await ReadAsync(stored, cancel);
        var status = XeroApi.Str(record["Status"]);
        if (status is "DELETED" or "VOIDED" or "DECLINED")
            throw new XeroException($"It is {status.ToLowerInvariant()} in Xero, so it can't be sent.");

        if (!IsQuote(stored) && status is "DRAFT" or "SUBMITTED")
        {
            try
            {
                await api.PostAsync($"Invoices/{stored.XeroId}", new JsonObject { ["InvoiceID"] = stored.XeroId, ["Status"] = "AUTHORISED" },
                    stored.Id + "-approve", cancel);
            }
            catch (XeroUncertainException)
            {
                // Fall through: reading it back says whether the approval landed.
            }
            record = await ReadAsync(stored, cancel);
            if (XeroApi.Str(record["Status"]) is not "AUTHORISED")
                throw new XeroException("Couldn't tell whether Xero approved it. Look at it in Xero, then try again.");
            stored = save(stored with { Approved = DateTimeOffset.UtcNow });
        }

        return await GatherAsync(stored, record, tradingName, cancel);
    }

    /// <summary>
    /// For a reminder or follow-up: reads the document from Xero first. When Xero says it's paid, accepted or
    /// declined, nothing is fetched; the document is marked settled and the reason returned instead.
    /// </summary>
    public async Task<(Ready? Ready, string? Settled)> PrepareChaserAsync(StoredDocument stored, string tradingName, CancellationToken cancel = default)
    {
        if (CannotSend(stored) is { } reason)
            throw new XeroException(reason);
        var record = await ReadAsync(stored, cancel);
        if (Chasers.SettledBy(stored.Document.Kind, XeroApi.Str(record["Status"]), Amount(record["AmountDue"])) is { } settled)
        {
            save(stored with { Settled = DateTimeOffset.UtcNow, SettledAs = settled });
            return (null, settled);
        }
        if (!IsQuote(stored) && XeroApi.Str(record["Status"]) is not "AUTHORISED")
            throw new XeroException("It isn't approved in Xero yet, so there's nothing to remind about.");
        var ready = await GatherAsync(stored, record, tradingName, cancel);
        if (ready.Values.DueOrExpiryDate is { } due && !IsQuote(stored))
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            ready = ready with { Values = ready.Values with { DaysOverdue = Math.Max(0, today.DayNumber - due.DayNumber) } };
        }
        return (ready, null);
    }

    /// <summary>What a statement mail needs: the contact as Xero has it and their unpaid invoices.</summary>
    public sealed record StatementReady(string ContactName, string? FirstName, string? Email, IReadOnlyList<StatementLine> Lines);

    /// <summary>
    /// Reads a customer's approved, unpaid invoices from Xero. Uses the linked contact when there is one,
    /// otherwise finds the contact by name. Nothing in Xero is changed.
    /// </summary>
    public async Task<StatementReady> PrepareStatementAsync(string customer, string? contactId, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(contactId))
        {
            var key = XeroRules.Normalise(customer);
            var matches = (await api.ContactsAsync(cancel)).Where(c => XeroRules.Normalise(c.Name) == key).ToList();
            if (matches.Count == 0)
                throw new XeroException($"Couldn't find {customer} in Xero's contacts.");
            if (matches.Count > 1)
                throw new XeroException($"Xero has more than one contact called {customer}. Link the client to the right one first.");
            contactId = matches[0].Id;
        }
        var contact = (await api.GetAsync($"Contacts/{contactId}", cancel))["Contacts"]?[0] as JsonObject
            ?? throw new XeroException($"Couldn't read {customer} from Xero.");
        var rows = await api.AllAsync("Invoices", "&ContactIDs=" + contactId + "&Statuses=AUTHORISED&where=" + Uri.EscapeDataString("Type==\"ACCREC\""), cancel);
        var lines = rows.Select(r => new StatementLine(
                XeroApi.Str(r["InvoiceNumber"]),
                Date(r["DateString"]),
                Date(r["DueDateString"]),
                Amount(r["Total"]) ?? 0,
                Amount(r["AmountDue"]) ?? 0,
                XeroApi.Str(r["Reference"]) is { Length: > 0 } reference ? reference : null))
            .Where(l => l.AmountDue > 0).ToList();
        var email = XeroApi.Str(contact["EmailAddress"]);
        return new StatementReady(
            XeroApi.Str(contact["Name"]) is { Length: > 0 } name ? name : customer,
            XeroApi.Str(contact["FirstName"]) is { Length: > 0 } first ? first : null,
            email.Length > 0 ? email : null,
            lines);
    }

    /// <summary>After a reminder or follow-up went out. Kept on this PC only; Xero isn't changed.</summary>
    public StoredDocument MarkChased(StoredDocument stored) =>
        save(stored with { Chased = DateTimeOffset.UtcNow, ChaseCount = stored.ChaseCount + 1 });

    // The PDF, online link, contact and amounts, all as Xero has them now.
    private async Task<Ready> GatherAsync(StoredDocument stored, JsonObject record, string tradingName, CancellationToken cancel)
    {
        string? link = null;
        if (!IsQuote(stored))
        {
            var online = await api.GetAsync($"Invoices/{stored.XeroId}/OnlineInvoice", cancel);
            link = XeroApi.Str(online["OnlineInvoices"]?[0]?["OnlineInvoiceUrl"]) is { Length: > 0 } url ? url : null;
        }
        var pdf = await api.PdfAsync($"{Resource(stored)}/{stored.XeroId}", cancel);

        var number = XeroApi.Str(record[IsQuote(stored) ? "QuoteNumber" : "InvoiceNumber"]) is { Length: > 0 } n ? n : stored.XeroNumber ?? "";
        var contactId = XeroApi.Str(record["Contact"]?["ContactID"]);
        var contact = contactId.Length > 0 ? (await api.GetAsync($"Contacts/{contactId}", cancel))["Contacts"]?[0] as JsonObject : null;
        var values = new DocumentEmailValues
        {
            Kind = stored.Document.Kind,
            ContactName = XeroApi.Str(record["Contact"]?["Name"]) is { Length: > 0 } name ? name : stored.Document.Customer.Name,
            FirstName = contact is null ? null : XeroApi.Str(contact["FirstName"]),
            Number = number,
            Total = Amount(record["Total"]) ?? stored.Document.Total ?? 0,
            AmountDue = IsQuote(stored) ? null : Amount(record["AmountDue"]),
            DueOrExpiryDate = Date(record[IsQuote(stored) ? "ExpiryDateString" : "DueDateString"]) ?? stored.Document.DueOrExpiryDate,
            Reference = XeroApi.Str(record["Reference"]) is { Length: > 0 } r ? r : stored.Document.Reference,
            OnlineLink = link,
            TradingName = tradingName,
        };
        var email = contact is null ? null : XeroApi.Str(contact["EmailAddress"]);
        return new Ready(stored, pdf, (IsQuote(stored) ? "Quote " : "Invoice ") + number + ".pdf", values,
            string.IsNullOrWhiteSpace(email) ? stored.Document.Customer.Email : email);
    }

    /// <summary>
    /// After the mail went out: marks an invoice as sent in Xero, or a quote as SENT. A failure here is
    /// reported, not retried; the mail itself has already gone.
    /// </summary>
    public async Task<StoredDocument> MarkSentAsync(StoredDocument stored, CancellationToken cancel = default)
    {
        stored = save(stored with { Emailed = DateTimeOffset.UtcNow });
        if (IsQuote(stored))
        {
            var record = await ReadAsync(stored, cancel);
            if (XeroApi.Str(record["Status"]) == "DRAFT")
                await api.PostAsync($"Quotes/{stored.XeroId}", new JsonObject
                {
                    ["QuoteID"] = stored.XeroId,
                    ["Status"] = "SENT",
                    ["Contact"] = new JsonObject { ["ContactID"] = XeroApi.Str(record["Contact"]?["ContactID"]) },
                    ["Date"] = XeroPayload.NormalizeDate(XeroApi.Str(record["DateString"])),
                }, stored.Id + "-sent", cancel);
        }
        else
            await api.PostAsync($"Invoices/{stored.XeroId}", new JsonObject { ["InvoiceID"] = stored.XeroId, ["SentToContact"] = true },
                stored.Id + "-sent-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cancel);
        return stored;
    }

    private async Task<JsonObject> ReadAsync(StoredDocument stored, CancellationToken cancel) =>
        (await api.GetAsync($"{Resource(stored)}/{stored.XeroId}", cancel))[Resource(stored)]?[0] as JsonObject
        ?? throw new XeroException("Xero didn't send it back. It may have been deleted in Xero.");

    private static decimal? Amount(JsonNode? node) => node is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : null;

    private static DateOnly? Date(JsonNode? node) =>
        XeroPayload.NormalizeDate(XeroApi.Str(node)) is { } iso ? DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
}
