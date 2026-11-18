namespace Webignite.Core.Clients;

/// <summary>What a mail to a client is about, as each kind can go to its own person there.</summary>
public enum MailFor
{
    Quotes,
    Invoices,
    Statements,
    Projects,
}

/// <summary>
/// Who at a client gets which mail. Invoices go to one person; quotes and statements go there too unless given their
/// own; mail about the work goes to the contact person unless given its own.
/// </summary>
public static class ClientMailTo
{
    public static readonly IReadOnlyList<MailFor> Kinds = [MailFor.Quotes, MailFor.Invoices, MailFor.Statements, MailFor.Projects];

    /// <summary>The person this kind of mail goes to, or null when nobody was picked.</summary>
    public static string? ContactId(Client client, MailFor kind) => kind switch
    {
        MailFor.Quotes => client.QuoteContactId ?? client.BillingContactId,
        MailFor.Invoices => client.BillingContactId,
        MailFor.Statements => client.StatementContactId ?? client.BillingContactId,
        _ => client.ProjectContactId ?? client.MainContactId,
    };

    /// <summary>Their address for this kind of mail, or null when nobody with an address was picked.</summary>
    public static string? Address(Client client, MailFor kind, Func<string, Person?> person) =>
        ContactId(client, kind) is { } id && person(id)?.Email is { Length: > 0 } email ? email : null;

    /// <summary>The client with this kind of mail going to this person.</summary>
    public static Client Send(Client client, MailFor kind, string contactId) => kind switch
    {
        MailFor.Quotes => client with { QuoteContactId = contactId },
        MailFor.Invoices => client with
        {
            BillingContactId = contactId,
            // Quotes and statements that followed the invoices stay where they were going.
            QuoteContactId = client.QuoteContactId ?? (client.BillingContactId == contactId ? null : client.BillingContactId),
            StatementContactId = client.StatementContactId ?? (client.BillingContactId == contactId ? null : client.BillingContactId),
        },
        MailFor.Statements => client with { StatementContactId = contactId },
        _ => client with { ProjectContactId = contactId },
    };

    /// <summary>Whether anything on the client points at this person.</summary>
    public static bool PointsAt(Client client, string contactId) =>
        client.MainContactId == contactId || client.BillingContactId == contactId || client.QuoteContactId == contactId
        || client.StatementContactId == contactId || client.ProjectContactId == contactId;

    /// <summary>The client with nothing pointing at a person who has left.</summary>
    public static Client Without(Client client, string contactId) => !PointsAt(client, contactId) ? client : client with
    {
        MainContactId = Other(client.MainContactId, contactId),
        BillingContactId = Other(client.BillingContactId, contactId),
        QuoteContactId = Other(client.QuoteContactId, contactId),
        StatementContactId = Other(client.StatementContactId, contactId),
        ProjectContactId = Other(client.ProjectContactId, contactId),
    };

    /// <summary>What this person gets, in words: "quotes, invoices". Empty when nothing.</summary>
    public static string Gets(Client client, string contactId) =>
        string.Join(", ", Kinds.Where(k => ContactId(client, k) == contactId).Select(Word));

    public static string Word(MailFor kind) => kind switch
    {
        MailFor.Quotes => "quotes",
        MailFor.Invoices => "invoices",
        MailFor.Statements => "statements and reminders",
        _ => "project mail",
    };

    private static string? Other(string? id, string gone) => id == gone ? null : id;
}
