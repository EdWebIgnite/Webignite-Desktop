using System.Text.Json.Nodes;
using Webignite.Core.Documents;
using Webignite.Core.Xero;

namespace Webignite.Xero;

/// <summary>Which Xero account sales go to, and the tax type (NONE, as Webignite is not VAT registered).</summary>
/// <summary>Where a draft goes in Xero: its sales account, tax rate and, when one is chosen, its branding theme.</summary>
public sealed record XeroTarget(string AccountCode, string TaxType = "NONE", string? BrandingThemeId = null);

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
        if (stored.Xero.State != XeroWriteState.None)
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

        // Lines with an item name are linked to that item in Xero's Products and Services (added there by name
        // when it isn't yet), so Xero's Item column shows it. Kept on the document, so it isn't looked up again.
        if (doc.Lines.Any(l => !string.IsNullOrWhiteSpace(l.Product) && string.IsNullOrEmpty(l.ItemCode)))
        {
            try
            {
                doc = await WithItemsAsync(doc, stored.Id, cancel);
            }
            catch (XeroException e)
            {
                return new Result.Stopped(stored, "Xero didn't let Webignite add the items to Products and Services. If Xero was connected "
                    + "before items were added, disconnect and connect Xero again in Settings, so it may manage items, then try again. (" + e.Message + ")");
            }
            stored = save(stored with { Document = doc });
        }

        // From here on, every step is saved before the next, so a crash or a dropped line leaves a clear trail.
        var payload = XeroPayload.Build(doc, contact?.Id ?? "", target.AccountCode, target.TaxType, doc.Number, target.BrandingThemeId);
        stored = save(stored with { Xero = stored.Xero with { State = XeroWriteState.Writing, ContactId = contact?.Id, Sent = payload.ToJsonString(), Problems = [] } });

        try
        {
            if (contact is null)
            {
                var newContact = XeroPayload.NewContact(doc.Customer, customerEmail);
                var made = await api.PutAsync("Contacts", new JsonObject { ["Contacts"] = new JsonArray(newContact) }, stored.Id + "-contact", cancel);
                var id = XeroApi.Str(made["Contacts"]?[0]?["ContactID"]);
                if (id.Length == 0)
                    throw new XeroUncertainException("Xero didn't say which contact it made. Check Xero.");
                payload["Contact"] = new JsonObject { ["ContactID"] = id };
                stored = save(stored with { Xero = stored.Xero with { ContactId = id, Sent = payload.ToJsonString() } });
            }

            var created = await api.PutAsync(Resource(doc) + "?unitdp=4", new JsonObject { [Resource(doc)] = new JsonArray(JsonNode.Parse(payload.ToJsonString())) },
                stored.Id + "-document", cancel);
            var record = created[Resource(doc)]?[0] as JsonObject ?? throw new XeroUncertainException("Xero's reply had no document in it. Use Check Xero.");
            stored = save(stored with
            {
                Xero = stored.Xero with
            {
                State = XeroWriteState.CreatedUnverified,
                Id = XeroApi.Str(record[IdKey(doc)]),
                Number = XeroApi.Str(record[NumberKey(doc)]) is { Length: > 0 } n ? n : null,
            },
            });
        }
        catch (XeroUncertainException e)
        {
            stored = save(stored with { Xero = stored.Xero with { State = XeroWriteState.Uncertain, Problems = [e.Message] } });
            return new Result.Stopped(stored, e.Message);
        }
        catch (XeroException e)
        {
            // Xero turned the document down, so nothing was made (a new contact may have been).
            stored = save(stored with { Xero = stored.Xero with { State = XeroWriteState.None, Sent = null, Problems = [e.Message] } });
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
        if (stored.Xero.Id is null)
        {
            if (stored.Xero.ContactId is null)
                return Unsure(stored, "Couldn't tell whether a new contact was made. Look in Xero for this customer and their document, then use Start over if nothing is there.");
            var existing = await api.ExistingAsync(doc.Kind == DocumentKind.Quote, stored.Xero.ContactId, cancel);
            if (XeroRules.FindOurs(doc, stored.Xero.ContactId, existing, linkedXeroIds()) is not { } ours)
                return Unsure(stored, "Couldn't find exactly one matching draft in Xero. Look in Xero, then use Start over if it isn't there.");
            stored = save(stored with { Xero = stored.Xero with { Id = ours.Id, Number = ours.Number, State = XeroWriteState.CreatedUnverified } });
        }
        return await VerifyAsync(stored, cancel);
    }

    private async Task<Document> WithItemsAsync(Document doc, string documentId, CancellationToken cancel)
    {
        var items = (await api.ItemsAsync(cancel)).ToList();
        var lines = new List<Line>();
        foreach (var line in doc.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Product) || !string.IsNullOrEmpty(line.ItemCode))
            {
                lines.Add(line);
                continue;
            }
            if (XeroItems.Match(line.Product, items) is not { } item)
            {
                item = XeroItems.New(line.Product, items);
                await api.PutAsync("Items", new JsonObject
                {
                    ["Items"] = new JsonArray(new JsonObject { ["Code"] = item.Code, ["Name"] = item.Name, ["IsSold"] = true, ["IsPurchased"] = false }),
                }, $"{documentId}-item-{item.Code}", cancel);
                items.Add(item);
            }
            lines.Add(line with { ItemCode = item.Code });
        }
        return doc with { Lines = lines };
    }

    /// <summary>After a person has checked Xero and found nothing, the document can be sent again.</summary>
    public StoredDocument StartOver(StoredDocument stored) =>
        stored.Xero.Id is not null ? stored : save(stored with { Xero = stored.Xero with { State = XeroWriteState.None, Sent = null, Problems = [] } });

    private Result.Stopped Unsure(StoredDocument stored, string message)
    {
        stored = save(stored with { Xero = stored.Xero with { State = XeroWriteState.Uncertain, Problems = [message] } });
        return new Result.Stopped(stored, message);
    }

    private async Task<Result> VerifyAsync(StoredDocument stored, CancellationToken cancel)
    {
        var doc = stored.Document;
        if (stored.Xero.Sent is null || JsonNode.Parse(stored.Xero.Sent) is not JsonObject sent)
            return Unsure(stored, "What was sent to Xero wasn't kept, so it can't be checked. Compare it in Xero by hand.");
        var reply = await api.GetAsync($"{Resource(doc)}/{stored.Xero.Id}?unitdp=4", cancel);
        var actual = reply[Resource(doc)]?[0] as JsonObject ?? throw new XeroException("Xero didn't send the document back.");
        var differences = XeroPayload.Verify(doc, sent, actual);
        stored = save(stored with
        {
            Status = DocumentStatus.InXero,
            Xero = stored.Xero with
            {
                Number = XeroApi.Str(actual[NumberKey(doc)]) is { Length: > 0 } n ? n : stored.Xero.Number,
                State = differences.Count == 0 ? XeroWriteState.Verified : XeroWriteState.Mismatch,
                Problems = differences,
            },
        });
        return differences.Count == 0
            ? new Result.Done(stored)
            : new Result.Stopped(stored, "Xero saved it, but it differs from this document: " + string.Join(" ", differences) + " Fix it in Xero.");
    }
}
