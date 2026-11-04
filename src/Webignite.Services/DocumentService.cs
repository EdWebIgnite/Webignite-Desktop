using Webignite.Core.Documents;
using Webignite.Core.Xero;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// Reading and saving quotes and invoices, for every page. Every change is made to the document as it is
/// stored now (<see cref="LocalStore.Update"/>), so an editor, the payment check and emailing can't undo each
/// other's work, and nothing that has gone to Xero is changed here by mistake.
/// </summary>
public sealed class DocumentService(LocalStore store)
{
    public StoredDocument? Get(string id) => store.Get(id);

    /// <summary>The document as stored now, or the copy given when it's gone.</summary>
    public StoredDocument Latest(StoredDocument stored) => store.Get(stored.Id) ?? stored;

    /// <summary>Newest first, optionally only quotes or only invoices.</summary>
    public IReadOnlyList<StoredDocument> List(DocumentKind? kind = null) => store.List(kind);

    /// <summary>A new draft on this PC.</summary>
    public StoredDocument Create(Document document) =>
        store.Save(new StoredDocument { Id = StoredDocument.NewId(), Document = document });

    /// <summary>
    /// Saves what was typed in the editor, but only while the document is still just on this PC. Once it has gone
    /// to Xero, Xero holds the real copy: the stored document is returned unchanged, and the caller says so.
    /// A document that was deleted meanwhile is saved again, so the typing isn't lost.
    /// </summary>
    public StoredDocument SaveDraft(string id, Document document) =>
        store.Update(id, current => current.XeroState == XeroWriteState.None ? current with { Document = document } : null)
        ?? store.Save(new StoredDocument { Id = id, Document = document });

    /// <summary>A new draft copied from another: the same customer, lines and notes, with fresh dates.</summary>
    public StoredDocument Copy(StoredDocument source, DocumentKind kind, DocumentSettings settings, DateOnly today) =>
        store.Save(DocumentCopies.Copy(source, kind, settings, today));

    /// <summary>Changes a document as stored now. Null when it was deleted.</summary>
    public StoredDocument? Change(string id, Func<StoredDocument, StoredDocument?> change) => store.Update(id, change);

    /// <summary>
    /// Throws away something Claude or ChatGPT prepared, as long as it never reached Xero. Returns what was
    /// thrown away, for Undo, or null when it can't be.
    /// </summary>
    public StoredDocument? Reject(WaitingItem item)
    {
        if (!Approvals.CanReject(item))
            return null;
        var gone = Latest(item.Stored);
        if (gone.XeroId is not null)
            return null;
        store.DeleteDocument(gone.Id);
        return gone;
    }

    /// <summary>
    /// Saves this exact record. Only for the Xero write steps, which hold the document (and keep the editor busy)
    /// from the first step to the last, saving each one before the next.
    /// </summary>
    public StoredDocument Put(StoredDocument stored) => store.Save(stored);

    /// <summary>Puts a rejected document back, for Undo.</summary>
    public StoredDocument Restore(StoredDocument stored) => store.Save(stored);

    /// <summary>Ticks off a document's open follow-up and reminder to-dos, once the mail went or it's settled.</summary>
    public void TickOffChasing(string id, DateTimeOffset now) => store.TickOffChasing(id, now);
}
