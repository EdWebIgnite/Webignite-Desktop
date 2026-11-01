using Webignite.Core.Calendar;
using Webignite.Core.Documents;
using Webignite.Core.Tasks;

namespace Webignite.Core.Clients;

/// <summary>Everything the app knows about one client, gathered by name from documents, tasks and events.</summary>
public sealed record ClientSummary(
    Client Client,
    IReadOnlyList<StoredDocument> Documents,
    IReadOnlyList<TaskItem> OpenTasks,
    IReadOnlyList<CalendarEvent> UpcomingEvents)
{
    /// <summary>What's still owed on this client's invoices, using Xero's payments when they've been read.</summary>
    public decimal Owed => Receivables.Owed(Documents);

    public DateTimeOffset? LastActivity => Documents.Count == 0 ? null : Documents.Max(d => d.Updated);
}

public static class ClientDirectory
{
    /// <summary>Names match ignoring case and extra spaces, so "Karoo Coffee  roasters" is the same client.</summary>
    public static string Key(string? name) =>
        string.Join(' ', (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    public static bool Matches(Client client, string? name) => Key(client.Name) == Key(name) && Key(name).Length > 0;

    public static ClientSummary Summarise(Client client, IEnumerable<StoredDocument> documents, IEnumerable<TaskItem> tasks,
        IEnumerable<CalendarEvent> events, DateOnly today) => new(
            client,
            documents.Where(d => Matches(client, d.Document.Customer.Name)).OrderByDescending(d => d.Updated).ToList(),
            TaskGroups.Sort(tasks.Where(t => !t.Done && Matches(client, t.Customer))).ToList(),
            events.Where(e => e.Date >= today && Matches(client, e.Customer)).OrderBy(e => e.Date).ThenBy(e => e.Start).ToList());

    /// <summary>What is wrong with a client before it can be saved, in plain English. Empty means fine.</summary>
    public static IReadOnlyList<string> Check(Client client, IEnumerable<Client> others)
    {
        var problems = new List<string>();
        if (Key(client.Name).Length == 0)
            problems.Add("Give the client a name.");
        else if (others.Any(o => o.Id != client.Id && Matches(o, client.Name)))
            problems.Add($"There is already a client called {client.Name.Trim()}.");
        if (!string.IsNullOrWhiteSpace(client.Email) && !LooksLikeEmail(client.Email.Trim()))
            problems.Add("The email address doesn't look right.");
        return problems;
    }

    private static bool LooksLikeEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at == email.LastIndexOf('@') && email.IndexOf('.', at) > at + 1
            && !email.EndsWith('.') && !email.Contains(' ');
    }

    /// <summary>
    /// Customer names used on documents that have no client page yet, so existing work can become
    /// clients with one click instead of being typed in again.
    /// </summary>
    public static IReadOnlyList<string> UnsavedNames(IEnumerable<Client> clients, IEnumerable<StoredDocument> documents)
    {
        var known = clients.Select(c => Key(c.Name)).ToHashSet();
        return documents
            .Select(d => d.Document.Customer.Name.Trim())
            .Where(name => name.Length > 0 && !known.Contains(Key(name)))
            .GroupBy(Key)
            .Select(g => g.First())
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
