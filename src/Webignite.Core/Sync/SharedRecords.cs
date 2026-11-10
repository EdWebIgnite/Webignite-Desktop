using Webignite.Core.Clients;
using Webignite.Core.Documents;

namespace Webignite.Core.Sync;

/// <summary>
/// A quote, invoice, client or contact as it travels between the PCs: the record, when it was last changed, and when
/// it was deleted (then the record itself is left out). These don't carry stamps field by field as projects do, so
/// the later copy wins, with the care below so nothing that happened is lost.
/// </summary>
public sealed record Shared<T>(T? Value, DateTimeOffset Updated, DateTimeOffset? Deleted = null) where T : class;

public static class SharedMerge
{
    /// <summary>
    /// Two copies of the same record made into one. A deletion stands unless the other copy was changed after it;
    /// two live copies are combined by <paramref name="both"/>, which gets the older one first.
    /// </summary>
    public static Shared<T> Merge<T>(Shared<T> local, Shared<T> remote, Func<T, T, T> both) where T : class
    {
        var updated = local.Updated > remote.Updated ? local.Updated : remote.Updated;
        return (local, remote) switch
        {
            ({ Deleted: { } l }, { Deleted: { } r }) => new Shared<T>(null, updated, l > r ? l : r),
            ({ Deleted: { } l }, { Value: not null }) => remote.Updated > l ? remote : local,
            ({ Value: not null }, { Deleted: { } r }) => local.Updated > r ? local : remote,
            ({ Value: { } l }, { Value: { } r }) => new Shared<T>(remote.Updated > local.Updated ? both(l, r) : both(r, l), updated),
            _ => remote.Updated > local.Updated ? remote : local,
        };
    }

    /// <summary>
    /// A client: the copy changed later, except for what belongs to this PC: the folder picked by hand and the files
    /// linked where they live, which are paths on this PC.
    /// </summary>
    public static Client Client(Client local, Client older, Client newer) =>
        newer with { Folder = local.Folder, Files = local.Files };

    /// <summary>A contact: the copy changed later.</summary>
    public static Person Contact(Person older, Person newer) => newer;

    /// <summary>
    /// A quote or invoice: the copy changed later, but what happened to it is never undone by an older copy. Once
    /// approved, emailed, chased, settled or taken off To approve on either PC it stays so; the link to Xero keeps
    /// whichever copy got furthest with Xero; credit notes and attachments made on either PC are all kept.
    /// </summary>
    public static StoredDocument Document(StoredDocument older, StoredDocument newer) => newer with
    {
        Delivery = new Delivery
        {
            Approved = Latest(older.Delivery.Approved, newer.Delivery.Approved),
            Emailed = Latest(older.Delivery.Emailed, newer.Delivery.Emailed),
            EmailedTo = (older.Delivery.Emailed ?? DateTimeOffset.MinValue) > (newer.Delivery.Emailed ?? DateTimeOffset.MinValue)
                ? older.Delivery.EmailedTo : newer.Delivery.EmailedTo ?? older.Delivery.EmailedTo,
            Chased = Latest(older.Delivery.Chased, newer.Delivery.Chased),
            ChaseCount = Math.Max(older.Delivery.ChaseCount, newer.Delivery.ChaseCount),
        },
        Settled = newer.Settled ?? older.Settled,
        OffApprovals = newer.OffApprovals ?? older.OffApprovals,
        Xero = Further(older.Xero, newer.Xero),
        CreditNotes = [.. newer.CreditNotes.Concat(older.CreditNotes).DistinctBy(c => c.Id, StringComparer.OrdinalIgnoreCase)],
        Attachments = [.. newer.Attachments.Concat(older.Attachments).DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase)],
    };

    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : a > b ? a : b;

    // The newer link to Xero, unless the older one got further (it has Xero's id and the newer one doesn't).
    private static XeroLink Further(XeroLink older, XeroLink newer) => newer.Id is null && older.Id is not null ? older : newer;
}
