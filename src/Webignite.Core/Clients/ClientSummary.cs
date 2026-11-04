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
}

public static class ClientDirectory
{
    /// <summary>
    /// The one way names are compared everywhere (clients, Xero contacts, statements, payers): ignoring case
    /// and extra spaces, so "Karoo Coffee  roasters" is the same client. Lower case, as statement keys are stored.
    /// </summary>
    public static string Key(string? name) =>
        string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public static bool Matches(Client client, string? name) => Key(client.Name) == Key(name) && Key(name).Length > 0;

    /// <summary>The client with this name, if there is one.</summary>
    public static Client? Find(IEnumerable<Client> clients, string? name) => clients.FirstOrDefault(c => Matches(c, name));

    public static ClientSummary Summarise(Client client, IEnumerable<StoredDocument> documents, IEnumerable<TaskItem> tasks,
        IEnumerable<CalendarEvent> events, DateOnly today) => new(
            client,
            documents.Where(d => Matches(client, d.Document.Customer.Name)).OrderByDescending(d => d.Updated).ToList(),
            TaskGroups.Sort(tasks.Where(t => !t.Done && Matches(client, t.Customer))).ToList(),
            events.Where(e => e.Date >= today && Matches(client, e.Customer)).OrderBy(e => e.Date).ThenBy(e => e.Start).ToList());

    /// <summary>
    /// The line under the Clients title: "6 clients, R 4 500.00 owed by 2, 1 new site underway". Parts with nothing
    /// to say are left out.
    /// </summary>
    public static string Headline(IReadOnlyCollection<ClientSummary> clients)
    {
        var parts = new List<string> { clients.Count == 1 ? "1 client" : $"{clients.Count} clients" };
        var owing = clients.Where(c => c.Owed > 0).ToList();
        if (owing.Count > 0)
            parts.Add($"{Money.Rand(owing.Sum(c => c.Owed))} owed by {owing.Count}");
        var building = clients.Count(c => Onboarding.InProgress(c.Client));
        if (building > 0)
            parts.Add(building == 1 ? "1 new site underway" : $"{building} new sites underway");
        return string.Join(", ", parts);
    }

    /// <summary>
    /// The line under a short client list. Clients with no email address come first, so mail can reach them;
    /// otherwise a reminder that new names on documents are offered above the list.
    /// Null when there are no clients; the empty state covers that.
    /// </summary>
    public static ClientHint? Hint(IReadOnlyCollection<ClientSummary> clients)
    {
        if (clients.Count == 0)
            return null;
        var noEmail = clients.Where(c => string.IsNullOrWhiteSpace(c.Client.Email))
            .OrderBy(c => c.Client.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (noEmail.Count > 0)
        {
            var first = noEmail[0].Client;
            return noEmail.Count == 1
                ? new($"{first.Name.Trim()} has no email address saved yet.", "Add it", first)
                : new($"{noEmail.Count} clients have no email address saved yet.", $"Start with {first.Name.Trim()}", first);
        }
        return new("New names on quotes and invoices show up at the top, ready to save.", "New client", null);
    }

    /// <summary>What is wrong with a client before it can be saved, in plain English. Empty means fine.</summary>
    public static IReadOnlyList<string> Check(Client client, IEnumerable<Client> others)
    {
        var problems = new List<string>();
        if (Key(client.Name).Length == 0)
            problems.Add("Give the client a name.");
        else if (others.Any(o => o.Id != client.Id && Matches(o, client.Name)))
            problems.Add($"There is already a client called {client.Name.Trim()}.");
        // A client can have several addresses, separated by ; or a comma.
        var bad = Webignite.Core.Mail.Recipients.Parse(client.Email).Bad;
        if (bad.Count == 1)
            problems.Add($"\"{bad[0]}\" doesn't look like an email address.");
        else if (bad.Count > 1)
            problems.Add($"These don't look like email addresses: {string.Join(", ", bad)}.");
        return problems;
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

/// <summary>The line under a short client list. A null client means the action starts a new one.</summary>
public sealed record ClientHint(string Text, string ActionText, Client? Open);
