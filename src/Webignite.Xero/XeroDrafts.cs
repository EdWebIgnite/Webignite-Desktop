using System.Text.Json.Nodes;
using Webignite.Core.Documents;
using Webignite.Core.Xero;

namespace Webignite.Xero;

/// <summary>Which Xero account sales go to, and the tax type (NONE, as Webignite is not VAT registered).</summary>
public sealed record XeroTarget(string AccountCode, string TaxType = "NONE");

/// <summary>
/// Puts quotes and invoices into Xero as drafts, one careful step at a time. What is about to be sent
/// is saved first, every write is tried once, and what Xero saved is read back and compared. Nothing
/// here approves, sends or emails anything in Xero.
/// </summary>
public sealed class XeroDrafts(XeroApi api, Func<StoredDocument, StoredDocument> save, Func<IReadOnlyCollection<string>> linkedXeroIds)
{
    public abstract record Result(StoredDocument Stored)
    {
        public sealed record Done(StoredDocument Stored) : Result(Stored);
        public sealed record NeedsContact(StoredDocument Stored, ContactMatch Match) : Result(Stored);
        public sealed record Stopped(StoredDocument Stored, string Reason) : Result(Stored);
    }

    private static string Resource(Document doc) => doc.Kind == DocumentKind.Quote ? "Quotes" : "Invoices";
    private static string IdKey(Document doc) => doc.Kind == DocumentKind.Quote ? "QuoteID" : "InvoiceID";
    private static string NumberKey(Document doc) => doc.Kind == DocumentKind.Quote ? "QuoteNumber" : "InvoiceNumber";

    /// <summary>
    /// Creates the draft. Pass contactId when a person picked the contact, or createContact when they
    /// said to add the customer to Xero as a new contact.
    /// </summary>
    public async Task<Result> CreateAsync(StoredDocument stored, XeroTarget target, string? contactId = null, bool createContact = false,
        string? customerEmail = null, CancellationToken cancel = default)
    {
        if (stored.XeroState != XeroWriteState.None)
            return new Result.Stopped(stored, "This one has already been sent to Xero. Use Check Xero instead.");
        var doc = stored.Document;
        if (DocumentValidator.Validate(doc) is { Count: > 0 } problems)
            return new Result.Stopped(stored, string.Join(" ", problems));

        var contacts = await api.ContactsAsync(cancel);
        XeroContact? contact = null;
        if (contactId is not null)
        {
            contact = contacts.FirstOrDefault(c => c.Id == contactId);
            if (contact is not { Active: true })
                return new Result.Stopped(stored, "That Xero contact is missing or archived. Pick another.");
        }
        else
        {
            var match = XeroRules.MatchContact(doc.Customer.Name, customerEmail ?? doc.Customer.Email, contacts);
            if (match is ContactMatch.Use use)
                contact = use.Contact;
            else if (match is not ContactMatch.New || !createContact)
                return new Result.NeedsContact(stored, match);
        }

        if (contact is not null)
        {
            var existing = await api.ExistingAsync(doc.Kind == DocumentKind.Quote, contact.Id, cancel);
            if (XeroRules.Duplicate(doc, contact.Id, existing, linkedXeroIds()) is { } duplicate)
                return new Result.Stopped(stored, duplicate);
        }

        // From here on, every step is saved before the next, so a crash or a dropped line leaves a clear trail.
        var payload = XeroPayload.Build(doc, contact?.Id ?? "", target.AccountCode, target.TaxType);
        stored = save(stored with { XeroState = XeroWriteState.Writing, XeroContactId = contact?.Id, XeroSent = payload.ToJsonString(), XeroProblems = [] });

        try
        {
            if (contact is null)
            {
                var newContact = new JsonObject { ["Name"] = doc.Customer.Name.Trim() };
                if ((customerEmail ?? doc.Customer.Email) is { Length: > 0 } email)
                    newContact["EmailAddress"] = email.Trim();
                var made = await api.PutAsync("Contacts", new JsonObject { ["Contacts"] = new JsonArray(newContact) }, stored.Id + "-contact", cancel);
                var id = XeroApi.Str(made["Contacts"]?[0]?["ContactID"]);
                if (id.Length == 0)
                    throw new XeroUncertainException("Xero didn't say which contact it made. Check Xero.");
                payload["Contact"] = new JsonObject { ["ContactID"] = id };
                stored = save(stored with { XeroContactId = id, XeroSent = payload.ToJsonString() });
            }

            var created = await api.PutAsync(Resource(doc) + "?unitdp=4", new JsonObject { [Resource(doc)] = new JsonArray(JsonNode.Parse(payload.ToJsonString())) },
                stored.Id + "-document", cancel);
            var record = created[Resource(doc)]?[0] as JsonObject ?? throw new XeroUncertainException("Xero's reply had no document in it. Use Check Xero.");
            stored = save(stored with
            {
                XeroState = XeroWriteState.CreatedUnverified,
                XeroId = XeroApi.Str(record[IdKey(doc)]),
                XeroNumber = XeroApi.Str(record[NumberKey(doc)]) is { Length: > 0 } n ? n : null,
            });
        }
        catch (XeroUncertainException e)
        {
            stored = save(stored with { XeroState = XeroWriteState.Uncertain, XeroProblems = [e.Message] });
            return new Result.Stopped(stored, e.Message);
        }
        catch (XeroException e)
        {
            // Xero turned the document down, so nothing was made (a new contact may have been).
            stored = save(stored with { XeroState = XeroWriteState.None, XeroSent = null, XeroProblems = [e.Message] });
            return new Result.Stopped(stored, e.Message);
        }
        return await VerifyAsync(stored, cancel);
    }

    /// <summary>
    /// Finds out what happened to an earlier write: reads the document back from Xero (finding it first
    /// if the reply never arrived) and compares it with what was sent.
    /// </summary>
    public async Task<Result> CheckAsync(StoredDocument stored, CancellationToken cancel = default)
    {
        var doc = stored.Document;
        if (stored.XeroId is null)
        {
            if (stored.XeroContactId is null)
                return Unsure(stored, "Couldn't tell whether a new contact was made. Look in Xero for this customer and their document, then use Start over if nothing is there.");
            var existing = await api.ExistingAsync(doc.Kind == DocumentKind.Quote, stored.XeroContactId, cancel);
            if (XeroRules.FindOurs(doc, stored.XeroContactId, existing, linkedXeroIds()) is not { } ours)
                return Unsure(stored, "Couldn't find exactly one matching draft in Xero. Look in Xero, then use Start over if it isn't there.");
            stored = save(stored with { XeroId = ours.Id, XeroNumber = ours.Number, XeroState = XeroWriteState.CreatedUnverified });
        }
        return await VerifyAsync(stored, cancel);
    }

    /// <summary>After a person has checked Xero and found nothing, the document can be sent again.</summary>
    public StoredDocument StartOver(StoredDocument stored) =>
        stored.XeroId is not null ? stored : save(stored with { XeroState = XeroWriteState.None, XeroSent = null, XeroProblems = [] });

    private Result Unsure(StoredDocument stored, string message)
    {
        stored = save(stored with { XeroState = XeroWriteState.Uncertain, XeroProblems = [message] });
        return new Result.Stopped(stored, message);
    }

    private async Task<Result> VerifyAsync(StoredDocument stored, CancellationToken cancel)
    {
        var doc = stored.Document;
        if (stored.XeroSent is null || JsonNode.Parse(stored.XeroSent) is not JsonObject sent)
            return Unsure(stored, "What was sent to Xero wasn't kept, so it can't be checked. Compare it in Xero by hand.");
        var reply = await api.GetAsync($"{Resource(doc)}/{stored.XeroId}?unitdp=4", cancel);
        var actual = reply[Resource(doc)]?[0] as JsonObject ?? throw new XeroException("Xero didn't send the document back.");
        var differences = XeroPayload.Verify(doc, sent, actual);
        stored = save(stored with
        {
            XeroNumber = XeroApi.Str(actual[NumberKey(doc)]) is { Length: > 0 } n ? n : stored.XeroNumber,
            XeroState = differences.Count == 0 ? XeroWriteState.Verified : XeroWriteState.Mismatch,
            Status = DocumentStatus.InXero,
            XeroProblems = differences,
        });
        return differences.Count == 0
            ? new Result.Done(stored)
            : new Result.Stopped(stored, "Xero saved it, but it differs from this document: " + string.Join(" ", differences) + " Fix it in Xero.");
    }
}
