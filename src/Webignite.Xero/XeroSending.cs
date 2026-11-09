using System.Globalization;
using System.Text.Json.Nodes;
using Webignite.Core.Documents;
using Webignite.Core.Xero;

namespace Webignite.Xero;

/// <summary>
/// Gets a checked Xero draft ready to email from Webignite's own mailbox: approves invoices (only when a
/// person clicked to), downloads Xero's PDF and online link, and afterwards tells Xero it was sent.
/// Nothing here sends mail; that stays with the Send button. Each change goes through <c>update</c>, which applies
/// it to the document as it is stored now, so a payment the check found meanwhile isn't undone by an older copy.
/// </summary>
public sealed class XeroSending(XeroApi api, Func<StoredDocument, Func<StoredDocument, StoredDocument>, StoredDocument> update)
{
    /// <summary>Everything the email needs, read from Xero so it matches the PDF.</summary>
    public sealed record Ready(StoredDocument Stored, byte[] Pdf, string PdfName, DocumentEmailValues Values, string? ContactEmail);

    private static bool IsQuote(StoredDocument s) => s.Document.Kind == DocumentKind.Quote;
    private static string Resource(StoredDocument s) => IsQuote(s) ? "Quotes" : "Invoices";

    /// <summary>Whether it can be emailed yet, or why not.</summary>
    public static string? CannotSend(StoredDocument stored) =>
        stored.Xero.Id is null || stored.Xero.State is not (XeroWriteState.Verified or XeroWriteState.Mismatch)
            ? "Create it in Xero first, and let Webignite check it."
            : null;

    /// <summary>
    /// Approves an invoice in Xero (a quote stays as it is until it's sent) and gathers the PDF, online link
    /// and amounts. Approving is one try only; if the line drops, Xero is read again to see where it stands.
    /// </summary>
    /// <remarks>
    /// With <paramref name="brandingThemeId"/>, an invoice approved here takes that theme too, so one drafted before
    /// a theme was chosen still gets its payment services (Payfast's Pay now) on the online invoice.
    /// </remarks>
    public async Task<Ready> PrepareAsync(StoredDocument stored, string tradingName, string? brandingThemeId = null, CancellationToken cancel = default)
    {
        if (CannotSend(stored) is { } reason)
            throw new XeroException(reason);
        var record = await ReadAsync(stored, cancel);
        var status = XeroApi.Str(record["Status"]);
        if (status is "DELETED" or "VOIDED" or "DECLINED")
            throw new XeroException($"It is {status.ToLowerInvariant()} in Xero, so it can't be sent.");

        if (!IsQuote(stored) && XeroStatuses.IsDraft(status))
        {
            try
            {
                var approve = new JsonObject { ["InvoiceID"] = stored.Xero.Id, ["Status"] = XeroStatuses.Authorised };
                if (!string.IsNullOrEmpty(brandingThemeId))
                    approve["BrandingThemeID"] = brandingThemeId;
                await api.PostAsync($"Invoices/{stored.Xero.Id}", approve, stored.Id + "-approve", cancel);
            }
            catch (XeroUncertainException)
            {
                // Fall through: reading it back says whether the approval landed.
            }
            record = await ReadAsync(stored, cancel);
            if (XeroApi.Str(record["Status"]) is not XeroStatuses.Authorised)
                throw new XeroException("Couldn't tell whether Xero approved it. Look at it in Xero, then try again.");
            stored = update(stored, s => s with { Delivery = s.Delivery with { Approved = DateTimeOffset.UtcNow } });
        }

        return await GatherAsync(stored, record, tradingName, cancel);
    }

    /// <summary>
    /// For a reminder or follow-up: reads the document from Xero first. When Xero says it's paid, accepted or
    /// declined, nothing is fetched; the document is marked settled and the reason returned instead.
    /// </summary>
    public async Task<(Ready? Ready, Settlement? Settled)> PrepareChaserAsync(StoredDocument stored, string tradingName, CancellationToken cancel = default)
    {
        if (CannotSend(stored) is { } reason)
            throw new XeroException(reason);
        var record = await ReadAsync(stored, cancel);
        if (Chasers.SettledBy(stored.Document.Kind, XeroApi.Str(record["Status"]), Amount(record["AmountDue"])) is { } settled)
        {
            update(stored, s => s with { Settled = new SettledState { On = DateTimeOffset.UtcNow, As = settled } });
            return (null, settled);
        }
        if (!IsQuote(stored) && XeroApi.Str(record["Status"]) is not XeroStatuses.Authorised)
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
            var key = Webignite.Core.Clients.ClientDirectory.Key(customer);
            var matches = (await api.ContactsAsync(cancel)).Where(c => Webignite.Core.Clients.ClientDirectory.Key(c.Name) == key).ToList();
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
        update(stored, s => s with { Delivery = s.Delivery with { Chased = DateTimeOffset.UtcNow, ChaseCount = s.Delivery.ChaseCount + 1 } });

    // The PDF, online link, contact and amounts, all as Xero has them now.
    private async Task<Ready> GatherAsync(StoredDocument stored, JsonObject record, string tradingName, CancellationToken cancel)
    {
        string? link = null;
        if (!IsQuote(stored))
        {
            var online = await api.GetAsync($"Invoices/{stored.Xero.Id}/OnlineInvoice", cancel);
            link = XeroApi.Str(online["OnlineInvoices"]?[0]?["OnlineInvoiceUrl"]) is { Length: > 0 } url ? url : null;
        }
        var pdf = await api.PdfAsync($"{Resource(stored)}/{stored.Xero.Id}", cancel);

        var number = XeroApi.Str(record[IsQuote(stored) ? "QuoteNumber" : "InvoiceNumber"]) is { Length: > 0 } n ? n : stored.Xero.Number ?? "";
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
    public async Task<StoredDocument> MarkSentAsync(StoredDocument stored, IReadOnlyList<Webignite.Core.Mail.MailPerson>? sentTo = null, CancellationToken cancel = default)
    {
        stored = update(stored, s => s with
        {
            Delivery = s.Delivery with
            {
                Emailed = DateTimeOffset.UtcNow,
                EmailedTo = sentTo is { Count: > 0 } ? Webignite.Core.Mail.Recipients.Short(sentTo) : s.Delivery.EmailedTo,
            },
        });
        if (IsQuote(stored))
        {
            var record = await ReadAsync(stored, cancel);
            if (XeroApi.Str(record["Status"]) == XeroStatuses.Draft)
                await api.PostAsync($"Quotes/{stored.Xero.Id}", new JsonObject
                {
                    ["QuoteID"] = stored.Xero.Id,
                    ["Status"] = XeroStatuses.Sent,
                    ["Contact"] = new JsonObject { ["ContactID"] = XeroApi.Str(record["Contact"]?["ContactID"]) },
                    ["Date"] = XeroPayload.NormalizeDate(XeroApi.Str(record["DateString"])),
                }, stored.Id + "-sent", cancel);
        }
        else
            await api.PostAsync($"Invoices/{stored.Xero.Id}", new JsonObject { ["InvoiceID"] = stored.Xero.Id, ["SentToContact"] = true },
                stored.Id + "-sent-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cancel);
        return stored;
    }

    /// <summary>
    /// Tells Xero the client accepted or declined a sent quote, only when Eddie clicked to. When Xero already has an
    /// answer (set in Xero meanwhile), that one is kept here instead and nothing is written.
    /// </summary>
    public async Task<StoredDocument> DecideQuoteAsync(StoredDocument stored, bool accepted, CancellationToken cancel = default)
    {
        if (!QuoteOutcomes.CanDecide(stored))
            throw new XeroException("Only a quote that went to the client can be marked accepted or declined.");
        var record = await ReadAsync(stored, cancel);
        var status = XeroApi.Str(record["Status"]);
        if (Chasers.SettledBy(DocumentKind.Quote, status, null) is { } already)
            return update(stored, s => s with { Xero = s.Xero with { Status = status }, Settled = new SettledState { On = DateTimeOffset.UtcNow, As = already } });
        if (status != XeroStatuses.Sent)
            throw new XeroException("Xero only takes the client's answer on a sent quote. Email it first.");

        var wanted = accepted ? XeroStatuses.Accepted : XeroStatuses.Declined;
        try
        {
            await api.PostAsync($"Quotes/{stored.Xero.Id}", new JsonObject
            {
                ["QuoteID"] = stored.Xero.Id,
                ["Status"] = wanted,
                ["Contact"] = new JsonObject { ["ContactID"] = XeroApi.Str(record["Contact"]?["ContactID"]) },
                ["Date"] = XeroPayload.NormalizeDate(XeroApi.Str(record["DateString"])),
            }, stored.Id + "-" + wanted.ToLowerInvariant(), cancel);
        }
        catch (XeroUncertainException)
        {
            // Reading it back says whether it landed.
        }
        if (XeroApi.Str((await ReadAsync(stored, cancel))["Status"]) != wanted)
            throw new XeroException("Couldn't tell whether Xero took it. Look at the quote in Xero, then try again.");
        return update(stored, s => QuoteOutcomes.Decided(s, accepted, DateTimeOffset.UtcNow));
    }

    private async Task<JsonObject> ReadAsync(StoredDocument stored, CancellationToken cancel) =>
        (await api.GetAsync($"{Resource(stored)}/{stored.Xero.Id}", cancel))[Resource(stored)]?[0] as JsonObject
        ?? throw new XeroException("Xero didn't send it back. It may have been deleted in Xero.");

    private static decimal? Amount(JsonNode? node) => node is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : null;

    private static DateOnly? Date(JsonNode? node) =>
        XeroPayload.NormalizeDate(XeroApi.Str(node)) is { } iso ? DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
}
