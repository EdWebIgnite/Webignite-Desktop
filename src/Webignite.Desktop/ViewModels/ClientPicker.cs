using Webignite.Core.Clients;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The client lists pages offer: names for a box that suggests as you type, or a drop-down with a first
/// choice such as "All clients". Clients arrive sorted by name from the store.
/// </summary>
public static class ClientPicker
{
    public static IReadOnlyList<string> Names(IEnumerable<Client> clients) => [.. clients.Select(c => c.Name)];

    public static IReadOnlyList<Choice<Client?>> Choices(string first, IEnumerable<Client> clients) =>
        [new(first, null), .. clients.Select(c => new Choice<Client?>(c.Name, c))];

    /// <summary>The choice for the client picked before the list was rebuilt, or the first choice.</summary>
    public static Choice<Client?> Keep(IReadOnlyList<Choice<Client?>> choices, Choice<Client?>? before) =>
        choices.FirstOrDefault(c => c.Value is not null && c.Value.Id == before?.Value?.Id) ?? choices[0];
}
