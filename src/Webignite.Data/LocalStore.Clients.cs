using Webignite.Core.Clients;

namespace Webignite.Data;

/// <summary>Client pages. Everything but the id is encrypted.</summary>
public sealed partial class LocalStore
{
    private const string ClientContext = "client:";

    public Client SaveClient(Client client) => Put("clients", ClientContext, client.Id, client);

    public void DeleteClient(string id) => Remove("clients", id);

    /// <summary>One client by id, without unlocking all of them.</summary>
    public Client? GetClient(string id) => Find<Client>("clients", ClientContext, id);

    /// <summary>All clients, A to Z. Names are encrypted, so sorting happens after reading.</summary>
    public IReadOnlyList<Client> ListClients() =>
        ReadAll<Client>(ClientContext, "SELECT id, payload FROM clients").OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
}
