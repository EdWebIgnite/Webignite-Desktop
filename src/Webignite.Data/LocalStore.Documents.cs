using Webignite.Core.Documents;

namespace Webignite.Data;

/// <summary>Quotes and invoices. Kind, status and times are readable for lists; the document itself is encrypted.</summary>
public sealed partial class LocalStore
{
    private const string DocumentContext = "document:";

    public StoredDocument Save(StoredDocument stored)
    {
        var now = clock.GetUtcNow();
        var existing = Get(stored.Id);
        stored = stored with { Created = existing?.Created ?? now, Updated = now };
        Run("""
            INSERT INTO documents (id, kind, status, created, updated, payload)
            VALUES ($id, $kind, $status, $created, $updated, $payload)
            ON CONFLICT (id) DO UPDATE SET kind = $kind, status = $status, updated = $updated, payload = $payload
            """,
            ("$id", stored.Id), ("$kind", stored.Document.Kind.ToString()), ("$status", stored.Status.ToString()),
            ("$created", stored.Created.ToString("O")), ("$updated", stored.Updated.ToString("O")),
            ("$payload", Seal(DocumentContext + stored.Id, stored)));
        return stored;
    }

    /// <summary>
    /// Changes a document as it is in the database now, not as some screen last read it, so two changes made at
    /// once (a payment found by the check while an invoice is emailed, say) both stay. <paramref name="change"/>
    /// returns null to leave it as it is. Returns the document as it now stands, or null when it was deleted.
    /// </summary>
    public StoredDocument? Update(string id, Func<StoredDocument, StoredDocument?> change) => InOneGo(() =>
    {
        var current = Get(id);
        var changed = current is null ? null : change(current);
        return changed is null ? current : Save(changed with { Id = id });
    });

    public StoredDocument? Get(string id) => Find<StoredDocument>("documents", DocumentContext, id);

    /// <summary>Removes a document from this PC. Only for ones that never went to Xero.</summary>
    public void DeleteDocument(string id) => Remove("documents", id);

    /// <summary>Newest first.</summary>
    public IReadOnlyList<StoredDocument> List(DocumentKind? kind = null) => kind is null
        ? ReadAll<StoredDocument>(DocumentContext, "SELECT id, payload FROM documents ORDER BY updated DESC")
        : ReadAll<StoredDocument>(DocumentContext, "SELECT id, payload FROM documents WHERE kind = $kind ORDER BY updated DESC", ("$kind", kind.Value.ToString()));
}
