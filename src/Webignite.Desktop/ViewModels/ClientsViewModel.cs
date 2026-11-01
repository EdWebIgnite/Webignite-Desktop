using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

/// <summary>The Clients module: a searchable list, and one client's page when one is open.</summary>
public partial class ClientsViewModel : ViewModelBase
{
    private readonly AppServices services;
    private List<ClientRow> all = [];

    public ClientsViewModel(AppServices services)
    {
        this.services = services;
        Refresh();
    }

    public ObservableCollection<ClientRow> Rows { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing))]
    public partial ClientDetailViewModel? Detail { get; set; }

    public bool IsListing => Detail is null;
    public bool IsEmpty => all.Count == 0;
    public bool HasSuggestions => Suggestions.Count > 0;
    public bool NoMatches => all.Count > 0 && Rows.Count == 0;

    partial void OnSearchChanged(string value) => Filter();

    [RelayCommand]
    private void New() => Detail = new ClientDetailViewModel(new Client { Id = Client.NewId(), Name = "" }, isNew: true, services, Close);

    [RelayCommand]
    private void Open(ClientRow row) => Detail = new ClientDetailViewModel(row.Client, isNew: false, services, Close);

    [RelayCommand]
    private void AddSuggested(string name)
    {
        var client = services.Store.SaveClient(new Client { Id = Client.NewId(), Name = name, Created = DateTimeOffset.UtcNow });
        Refresh();
        Detail = new ClientDetailViewModel(client, isNew: false, services, Close);
    }

    private void Close()
    {
        Detail = null;
        Refresh();
    }

    public void Refresh()
    {
        Detail?.Refresh();
        var clients = services.Store.ListClients();
        var documents = services.Store.List();
        var tasks = services.Store.ListTasks();
        var today = DateOnly.FromDateTime(DateTime.Today);
        all = clients
            .Select(c => new ClientRow(ClientDirectory.Summarise(c, documents, tasks, [], today)))
            .ToList();
        Suggestions.Clear();
        foreach (var name in ClientDirectory.UnsavedNames(clients, documents))
            Suggestions.Add(name);
        Filter();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasSuggestions));
    }

    private void Filter()
    {
        var words = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Rows.Clear();
        foreach (var row in all.Where(r => words.All(r.Contains)))
            Rows.Add(row);
        OnPropertyChanged(nameof(NoMatches));
    }
}

/// <summary>One line in the clients list.</summary>
public sealed record ClientRow(ClientSummary Summary)
{
    public Client Client => Summary.Client;
    public string Name => Client.Name;
    public string Contact => string.Join("  ·  ", new[] { Client.ContactPerson, Client.Email, Client.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public bool HasContact => Contact.Length > 0;
    public string Owed => Summary.Owed == 0 ? "" : Money.Rand(Summary.Owed);
    public string Work
    {
        get
        {
            var quotes = Summary.Documents.Count(d => d.Document.Kind == DocumentKind.Quote);
            var invoices = Summary.Documents.Count(d => d.Document.Kind == DocumentKind.Invoice);
            var parts = new List<string>();
            if (quotes > 0) parts.Add(quotes == 1 ? "1 quote" : $"{quotes} quotes");
            if (invoices > 0) parts.Add(invoices == 1 ? "1 invoice" : $"{invoices} invoices");
            if (Summary.OpenTasks.Count > 0) parts.Add(Summary.OpenTasks.Count == 1 ? "1 to-do" : $"{Summary.OpenTasks.Count} to-dos");
            return parts.Count == 0 ? "Nothing yet" : string.Join(", ", parts);
        }
    }

    public bool Contains(string word) =>
        new[] { Client.Name, Client.ContactPerson, Client.Email, Client.Phone, Client.Website }
            .Any(s => s?.Contains(word, StringComparison.OrdinalIgnoreCase) == true);
}
