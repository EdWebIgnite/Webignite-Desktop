using Webignite.Core.Clients;
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

    /// <summary>A draft in Xero being changed here. Xero still has the old copy until Update draft in Xero.</summary>
    Editing,
}

/// <summary>Changing a draft that Webignite already put in Xero.</summary>
public static class XeroEdits
{
    /// <summary>
    /// A draft Webignite made in Xero that nobody has approved, emailed or settled yet, so it can be changed here
    /// and the change put into Xero. Ones made in Xero itself are changed in Xero.
    /// </summary>
    public static bool CanChange(StoredDocument stored) =>
        stored.Document.Origin != DocumentOrigin.Xero
        && stored.Xero.Id is not null
        && stored.Xero.State is XeroWriteState.Verified or XeroWriteState.Mismatch
        && stored.Delivery.Approved is null
        && stored.Delivery.Emailed is null
        && stored.Settled is null
        && (stored.Xero.Status is null || XeroStatuses.IsDraft(stored.Xero.Status));

    /// <summary>Starts changing it here, or null when it can't be changed (it moved on meanwhile).</summary>
    public static StoredDocument? StartChanging(StoredDocument stored) =>
        CanChange(stored) ? stored with { Xero = stored.Xero with { State = XeroWriteState.Editing, Problems = [] } } : null;
}

/// <summary>
/// One of the organisation's branding themes in Xero: the look of the PDF and the online invoice, and the payment
/// services offered on it (such as Payfast's Pay now button).
/// </summary>
public sealed record XeroBrandingTheme(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>An item in Xero's Products and Services: what fills the Item column on quotes and invoices.</summary>
public sealed record XeroItem(string Code, string Name);

/// <summary>Matching a line's item name to Xero's Products and Services, and naming new ones.</summary>
public static class XeroItems
{
    public const int LongestCode = 30;
    public const int LongestName = 50;

    /// <summary>The Xero item a line's item name means: one with that name or that code, any case.</summary>
    public static XeroItem? Match(string itemName, IEnumerable<XeroItem> items)
    {
        var name = itemName.Trim();
        return items.FirstOrDefault(i => string.Equals(i.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(i => string.Equals(i.Code.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A new item for a line's item name: the name itself as its code when it fits Xero's 30 letters, else cut at a
    /// word, and a number after it when another item already has that code.
    /// </summary>
    public static XeroItem New(string itemName, IEnumerable<XeroItem> existing)
    {
        var name = itemName.Trim();
        var code = name.Length <= LongestCode ? name : Cut(name, LongestCode);
        var taken = existing.Select(i => i.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unique = code;
        for (var n = 2; taken.Contains(unique); n++)
            unique = Cut(code, LongestCode - n.ToString(System.Globalization.CultureInfo.InvariantCulture).Length - 1) + " " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new XeroItem(unique, name.Length <= LongestName ? name : Cut(name, LongestName));
    }

    private static string Cut(string text, int most)
    {
        if (text.Length <= most)
            return text;
        var cut = text[..most];
        var space = cut.LastIndexOf(' ');
        return (space > most / 2 ? cut[..space] : cut).TrimEnd(' ', '&', '-', ',');
    }
}

/// <summary>A Xero account sales can be coded to, e.g. 200 Sales.</summary>
public sealed record XeroSalesAccount(string Code, string Name)
{
    public override string ToString() => $"{Code}  {Name}";
}

public static class XeroRules
{
    /// <summary>
    /// Matches on name or email. A match on both, or on one where the other is unknown, is used;
    /// anything less certain goes to a person. Archived contacts are never used silently.
    /// </summary>
    public static ContactMatch MatchContact(string customerName, string? customerEmail, IReadOnlyList<XeroContact> contacts)
    {
        var name = ClientDirectory.Key(customerName);
        // The customer may have several addresses; the first one is the one matched.
        var email = ClientDirectory.Key(Webignite.Core.Mail.ClientMail.First(customerEmail) ?? customerEmail);
        var matches = contacts
            .Where(c => ClientDirectory.Key(c.Name) == name || (email.Length > 0 && ClientDirectory.Key(c.Email) == email))
            .ToList();
        if (matches.Count == 0)
            return new ContactMatch.New();
        var active = matches.Where(c => c.Active).ToList();
        if (active.Count == 0)
            return matches.Count == 1 ? new ContactMatch.Archived(matches[0]) : new ContactMatch.Choose("These Xero contacts are archived. Restore one in Xero or pick another.", matches);
        if (active.Count > 1)
            return new ContactMatch.Choose("More than one Xero contact could be this customer. Pick the right one.", active);
        var one = active[0];
        var nameMatches = ClientDirectory.Key(one.Name) == name;
        var emailMatches = email.Length == 0 || ClientDirectory.Key(one.Email).Length == 0 || ClientDirectory.Key(one.Email) == email;
        return nameMatches && emailMatches
            ? new ContactMatch.Use(one)
            : new ContactMatch.Choose("A Xero contact matches on name or email, but not both. Check it's the same customer.", [one]);
    }

    /// <summary>Contacts whose name or email contains the search, for picking by hand.</summary>
    public static IReadOnlyList<XeroContact> Search(string text, IReadOnlyList<XeroContact> contacts)
    {
        var q = ClientDirectory.Key(text);
        return q.Length < 2 ? [] : contacts.Where(c => c.Active && (ClientDirectory.Key(c.Name).Contains(q) || ClientDirectory.Key(c.Email).Contains(q))).Take(20).ToList();
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
        var found = existing.Where(r => !claimed.Contains(r.Id) && r.ContactId == contactId && r.Status == XeroStatuses.Draft
            && r.Date == doc.Date && r.Total is { } t && doc.Total is { } mine && Money.Cents(t) == Money.Cents(mine)).ToList();
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>A Xero app's Client ID: 32 letters and numbers (hex), as developer.xero.com shows it.</summary>
    public static bool LooksLikeClientId(string? text) => text is { Length: 32 } id && id.All(Uri.IsHexDigit);

    /// <summary>Whether Xero has the "No VAT" tax rate (NONE) that drafts are written with.</summary>
    public static bool HasNoVatRate(IEnumerable<string> taxTypes) => taxTypes.Contains("NONE", StringComparer.Ordinal);

    /// <summary>Accounts Webignite's sales can go to.</summary>
    public static bool IsSalesAccount(string? type, string? status) =>
        status == "ACTIVE" && type is "REVENUE" or "SALES" or "OTHERINCOME";
}
