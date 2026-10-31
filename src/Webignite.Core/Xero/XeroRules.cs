using Webignite.Core.Documents;

namespace Webignite.Core.Xero;

/// <summary>A contact as Xero lists it.</summary>
public sealed record XeroContact(string Id, string Name, string? Email, bool Active);

/// <summary>A quote or invoice already in Xero, as far as duplicate checks need.</summary>
public sealed record XeroExisting(string Id, string? Number, string ContactId, DateOnly? Date, string? Currency, decimal? Total, string Status);

/// <summary>How a document's customer lines up with Xero's contacts.</summary>
public abstract record ContactMatch
{
    /// <summary>One clear match: use it.</summary>
    public sealed record Use(XeroContact Contact) : ContactMatch;

    /// <summary>Several possible matches, or a partial one: a person picks.</summary>
    public sealed record Choose(string Reason, IReadOnlyList<XeroContact> Candidates) : ContactMatch;

    /// <summary>Nobody in Xero matches. A new contact is only made when a person says so.</summary>
    public sealed record New : ContactMatch;

    /// <summary>The only match is archived in Xero.</summary>
    public sealed record Archived(XeroContact Contact) : ContactMatch;
}

/// <summary>Where a document's Xero write stands. Writes are never retried by themselves.</summary>
public enum XeroWriteState
{
    None,

    /// <summary>The write started. If the app stops here, Xero has to be checked before anything else.</summary>
    Writing,

    /// <summary>The connection broke during a write, so it may or may not be in Xero.</summary>
    Uncertain,

    /// <summary>Xero made it, but it hasn't been read back and compared yet.</summary>
    CreatedUnverified,

    /// <summary>Xero made it, but what it saved differs from the document.</summary>
    Mismatch,

    /// <summary>In Xero as a draft and checked line by line.</summary>
    Verified,
}

public static class XeroRules
{
    public static string Normalise(string? value) => string.Join(' ', (value ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Matches on name or email. A match on both, or on one where the other is unknown, is used;
    /// anything less certain goes to a person. Archived contacts are never used silently.
    /// </summary>
    public static ContactMatch MatchContact(string customerName, string? customerEmail, IReadOnlyList<XeroContact> contacts)
    {
        var name = Normalise(customerName);
        var email = Normalise(customerEmail);
        var matches = contacts
            .Where(c => Normalise(c.Name) == name || (email.Length > 0 && Normalise(c.Email) == email))
            .ToList();
        if (matches.Count == 0)
            return new ContactMatch.New();
        var active = matches.Where(c => c.Active).ToList();
        if (active.Count == 0)
            return matches.Count == 1 ? new ContactMatch.Archived(matches[0]) : new ContactMatch.Choose("These Xero contacts are archived. Restore one in Xero or pick another.", matches);
        if (active.Count > 1)
            return new ContactMatch.Choose("More than one Xero contact could be this customer. Pick the right one.", active);
        var one = active[0];
        var nameMatches = Normalise(one.Name) == name;
        var emailMatches = email.Length == 0 || Normalise(one.Email).Length == 0 || Normalise(one.Email) == email;
        return nameMatches && emailMatches
            ? new ContactMatch.Use(one)
            : new ContactMatch.Choose("A Xero contact matches on name or email, but not both. Check it's the same customer.", [one]);
    }

    /// <summary>Contacts whose name or email contains the search, for picking by hand.</summary>
    public static IReadOnlyList<XeroContact> Search(string text, IReadOnlyList<XeroContact> contacts)
    {
        var q = Normalise(text);
        return q.Length < 2 ? [] : contacts.Where(c => c.Active && (Normalise(c.Name).Contains(q) || Normalise(c.Email).Contains(q))).Take(20).ToList();
    }

    /// <summary>
    /// Stops a second copy: same customer, date, currency and total as something already in Xero
    /// that isn't deleted or voided. Returns the reason, or null when it's safe.
    /// </summary>
    public static string? Duplicate(Document doc, string contactId, IEnumerable<XeroExisting> existing, IReadOnlyCollection<string> ours)
    {
        foreach (var row in existing)
        {
            if (ours.Contains(row.Id) || row.ContactId != contactId || row.Status is "DELETED" or "VOIDED")
                continue;
            if (row.Date == doc.Date && row.Currency == doc.Currency && row.Total is { } total && doc.Total is { } mine
                && Money.Cents(total) == Money.Cents(mine))
                return $"Xero already has {row.Number ?? "a document"} for this customer with the same date and total. Check it in Xero before making another.";
        }
        return null;
    }

    /// <summary>
    /// After a broken write: finds the one draft in Xero that must be ours (same customer, date and
    /// total, still a draft, not linked to another document). Null when there isn't exactly one.
    /// </summary>
    public static XeroExisting? FindOurs(Document doc, string contactId, IEnumerable<XeroExisting> existing, IReadOnlyCollection<string> claimed)
    {
        var found = existing.Where(r => !claimed.Contains(r.Id) && r.ContactId == contactId && r.Status == "DRAFT"
            && r.Date == doc.Date && r.Total is { } t && doc.Total is { } mine && Money.Cents(t) == Money.Cents(mine)).ToList();
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>Accounts Webignite's sales can go to.</summary>
    public static bool IsSalesAccount(string? type, string? status) =>
        status == "ACTIVE" && type is "REVENUE" or "SALES" or "OTHERINCOME";
}
