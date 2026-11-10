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
    /// Saves what was typed in the editor, but only while the document is still just on this PC, or is a Xero draft
    /// being changed here. Otherwise Xero holds the real copy: the stored document is returned unchanged, and the caller says so.
    /// A document that was deleted meanwhile is saved again, so the typing isn't lost.
    /// </summary>
    public StoredDocument SaveDraft(string id, Document document) =>
        store.Update(id, current => current.Xero.State is XeroWriteState.None or XeroWriteState.Editing ? current with { Document = document } : null)
        ?? store.Save(new StoredDocument { Id = id, Document = document });

    /// <summary>
    /// Once one of Webignite's own is linked to its Xero copy, a copy of that same Xero document brought in from Xero
    /// meanwhile (before the link was known) is removed, so it isn't listed and counted twice. Returns how many went.
    /// </summary>
    public int DropImportedCopies(StoredDocument ours)
    {
        if (ours.Xero.Id is not { } xeroId || ours.Document.Origin == DocumentOrigin.Xero)
            return 0;
        var copies = store.List(ours.Document.Kind)
            .Where(d => d.Id != ours.Id && d.Document.Origin == DocumentOrigin.Xero && string.Equals(d.Xero.Id, xeroId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var copy in copies)
            store.DeleteDocument(copy.Id);
        return copies.Count;
    }

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
        if (gone.Xero.Id is not null)
            return null;
        store.DeleteDocument(gone.Id);
        return gone;
    }

    /// <summary>
    /// Saves this exact record. Only for the Xero write steps, which hold the document (and keep the editor busy)
    /// from the first step to the last, saving each one before the next.
    /// </summary>
    public StoredDocument Put(StoredDocument stored) => store.Save(stored);

    /// <summary>Takes something in Xero off To approve by hand; it stays in its list and Xero isn't changed. Null when it can't be.</summary>
    public StoredDocument? TakeOffApprovals(WaitingItem item, DateTimeOffset now) =>
        Approvals.CanRemove(item) ? store.Update(item.Stored.Id, s => s with { OffApprovals = now }) : null;

    /// <summary>Puts it back on To approve, for Undo.</summary>
    public StoredDocument? PutBackOnApprovals(string id) => store.Update(id, s => s with { OffApprovals = null });

    /// <summary>Puts a rejected document back, for Undo.</summary>
    public StoredDocument Restore(StoredDocument stored) => store.Save(stored);

    /// <summary>Ticks off a document's open follow-up and reminder to-dos, once the mail went or it's settled.</summary>
    public void TickOffChasing(string id, DateTimeOffset now) => store.TickOffChasing(id, now);
}
