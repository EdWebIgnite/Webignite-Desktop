using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>The Clients module: a searchable list, and one client's page when one is open.</summary>
public partial class ClientsViewModel : ViewModelBase, IRefreshable, IHoldsWork, ITabTitle
{
    public string? TabTitle => Detail?.Title;

    public bool HoldsWork => Detail is not null;

    private readonly AppServices services;
    private List<ClientRow> all = [];

    public ClientsViewModel(AppServices services)
    {
        this.services = services;
        Panels = new PanelsViewModel(services.Settings, "clients", [("list", "Client list"), ("detail", "Open client")], spine: "list");
        Refresh();
    }

    /// <summary>The client list and the open client side by side, moved, resized and hidden as on Mail.</summary>
    public PanelsViewModel Panels { get; }

    public ObservableCollection<ClientRow> Rows { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing), nameof(HasDetail), nameof(TabTitle))]
    public partial ClientDetailViewModel? Detail { get; set; }

    public bool IsListing => Detail is null;
    public bool HasDetail => Detail is not null;

    partial void OnDetailChanged(ClientDetailViewModel? value)
    {
        foreach (var row in all)
            row.IsOpen = value is not null && row.Client.Id == value.ClientId;
        // Opening a client brings its pane back if it was hidden.
        if (value is not null && !Panels["detail"].IsShown)
            Panels["detail"].IsShown = true;
    }

    /// <summary>Shows a client in the right-hand pane, saving the one open there first.</summary>
    private void Show(ClientDetailViewModel next)
    {
        if (Detail is { } open && !open.Leave())
            return;
        Detail = next;
    }

    /// <summary>The line under the title: "6 clients, R 4 500.00 owed by 2".</summary>
    [ObservableProperty]
    public partial string Headline { get; set; } = "";

    public bool IsEmpty => all.Count == 0;
    public bool HasSuggestions => Suggestions.Count > 0;
    public bool NoMatches => all.Count > 0 && Rows.Count == 0;

    /// <summary>The line under a short list, or null when the list is long, searched or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    public partial ClientHint? Hint { get; set; }

    public bool HasHint => Hint is not null;

    /// <summary>Does what the hint offers: opens the client it names, or starts a new one.</summary>
    [RelayCommand]
    private void FollowHint()
    {
        if (Hint?.Open is { } client)
            OpenClient(client);
        else
            New();
    }

    partial void OnSearchChanged(string value) => Filter();

    [RelayCommand]
    private void New() => Show(new ClientDetailViewModel(new Client { Id = Client.NewId(), Name = "" }, isNew: true, services, Close));

    [RelayCommand]
    private void Open(ClientRow row)
    {
        if (Detail?.ClientId != row.Client.Id)
            Show(new ClientDetailViewModel(row.Client, isNew: false, services, Close));
    }

    /// <summary>Opens a client's page, e.g. from search or after Undo brought it back.</summary>
    public void OpenClient(Client client)
    {
        Refresh();
        Show(new ClientDetailViewModel(client, isNew: false, services, Close));
    }

    /// <summary>What bringing clients in from Xero by hand did.</summary>
    [ObservableProperty] public partial string XeroMessage { get; set; } = "";

    [ObservableProperty] public partial bool BringingIn { get; set; }

    /// <summary>Xero's customers as clients now, rather than waiting for the next look.</summary>
    [RelayCommand]
    private async Task BringInFromXero()
    {
        BringingIn = true;
        XeroMessage = "Reading Xero's contacts…";
        try
        {
            var news = await services.Work.Run("Bringing clients in from Xero", () => services.XeroClients.RunAsync());
            XeroMessage = news.Problem ?? (news.Summary.Length > 0 ? news.Summary + "." : "Every customer in Xero is here already.");
            Refresh();
        }
        finally
        {
            BringingIn = false;
        }
    }

    [RelayCommand]
    private void AddSuggested(string name)
    {
        var client = services.Clients.Add(name);
        Refresh();
        Show(new ClientDetailViewModel(client, isNew: false, services, Close));
    }

    private void Close()
    {
        Detail = null;
        Refresh();
    }

    public void Refresh()
    {
        Detail?.Refresh();
        var clients = services.Clients.List();
        var documents = services.Documents.List();
        var tasks = services.Store.ListTasks();
        var today = DateOnly.FromDateTime(DateTime.Today);
        all = clients
            .Select(c => new ClientRow(ClientDirectory.Summarise(c, documents, tasks, [], today)) { IsOpen = Detail?.ClientId == c.Id })
            .ToList();
        Headline = ClientDirectory.Headline(all.Select(r => r.Summary).ToList());
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
        Hint = ListHints.Shows(Rows.Count, words.Length > 0) ? ClientDirectory.Hint(all.Select(r => r.Summary).ToList()) : null;
        OnPropertyChanged(nameof(NoMatches));
    }
}

/// <summary>One line in the clients list.</summary>
public sealed partial class ClientRow(ClientSummary summary) : ViewModelBase
{
    public ClientSummary Summary { get; } = summary;

    /// <summary>This client is the one open in the right-hand pane.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public Client Client => Summary.Client;
    public string Name => Client.Name;
    public string Contact => string.Join("  ·  ", new[] { Client.ContactPerson, Client.Email, Client.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public bool HasContact => Contact.Length > 0;
    public string Owed => Summary.Owed == 0 ? "" : Money.Rand(Summary.Owed);

    /// <summary>Some of what this client owes is past its due date: the row gets the bold edge.</summary>
    public bool HasOverdue => Summary.Documents.Any(d => Receivables.IsOverdue(d, DateOnly.FromDateTime(DateTime.Today)));
    public string? OverdueTip => HasOverdue ? Money.Rand(Receivables.Overdue(Summary.Documents, DateOnly.FromDateTime(DateTime.Today))) + " of it is overdue" : null;
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
            if (Onboarding.InProgress(Client)) parts.Add("new site: " + Onboarding.Progress(Client));
            return parts.Count == 0 ? "Nothing yet" : string.Join(", ", parts);
        }
    }

    public bool Contains(string word) =>
        new[] { Client.Name, Client.ContactPerson, Client.Email, Client.Phone, Client.Website }
            .Any(s => s?.Contains(word, StringComparison.OrdinalIgnoreCase) == true);
}
