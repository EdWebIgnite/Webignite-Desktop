using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Search;

namespace Webignite.Desktop.ViewModels;

/// <summary>Ctrl+K: one box that finds clients, quotes, invoices, mail, to-dos, notes, site logins, files and renewals.</summary>
public sealed partial class QuickSearchViewModel(AppServices services, Action<SearchHit> open) : ViewModelBase
{
    private SearchSources sources = new();

    public ObservableCollection<SearchHit> Results { get; } = [];

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial SearchHit? Chosen { get; set; }

    public bool NothingFound => Query.Trim().Length > 0 && Results.Count == 0;
    public bool ShowHint => Query.Trim().Length == 0;

    [RelayCommand]
    public void Show()
    {
        // Read once per opening, so typing stays quick. Mail has to be unlocked message by message, so it is read
        // off the screen's thread and joins the results when it's in; the box opens straight away.
        var accounts = services.Store.ListMailAccounts().Select(a => a.Id).ToList();
        sources = new SearchSources
        {
            Clients = services.Clients.List(),
            Documents = services.Documents.List(),
            Mail = [],
            Tasks = services.Store.ListTasks(),
            Renewals = services.Store.ListRenewals(),
            Notes = services.Notes.List(),
            Logins = services.Clients.AllLogins(),
        };
        Query = "";
        Results.Clear();
        IsOpen = true;
        Refresh();
        MailLoaded = LoadMail(accounts, sources);
        MailLoaded.Forget("Reading mail for search");
        FilesLoaded = LoadFiles(Files.FileOps.ClientRoot(services.Settings.Files), services.Settings.FileRecents);
        FilesLoaded.Forget("Reading file names for search");
    }

    /// <summary>Done once file names have joined the search.</summary>
    public Task FilesLoaded { get; private set; } = Task.CompletedTask;

    // Waits for its turn after mail, so the two never replace each other's results.
    private async Task LoadFiles(string? root, IReadOnlyList<string> recents)
    {
        var opened = sources;
        var files = await Files.FileIndex.ReadAsync(root, recents);
        await MailLoaded;
        if (!IsOpen || !ReferenceEquals(sources.Notes, opened.Notes))
            return;
        sources = sources with { Files = files };
        Refresh();
    }

    /// <summary>Done once the mail has joined the search, e.g. for the screenshot tool to wait on.</summary>
    public Task MailLoaded { get; private set; } = Task.CompletedTask;

    private async Task LoadMail(IReadOnlyList<string> accounts, SearchSources opened)
    {
        var mail = await Task.Run(() => services.Store.FindMail(accounts, _ => true, 1500));
        if (!IsOpen || !ReferenceEquals(sources, opened))
            return; // closed, or opened again since
        sources = opened with { Mail = mail };
        Refresh();
    }

    // The file names and mail are let go when the box closes; they're read again next time.
    public void Hide()
    {
        IsOpen = false;
        sources = new SearchSources();
        Results.Clear();
    }

    partial void OnQueryChanged(string value) => Refresh();

    private void Refresh()
    {
        Results.Clear();
        foreach (var hit in QuickSearch.Find(Query, sources))
            Results.Add(hit);
        Chosen = Results.FirstOrDefault();
        OnPropertyChanged(nameof(NothingFound));
        OnPropertyChanged(nameof(ShowHint));
    }

    public void Move(int step)
    {
        if (Results.Count == 0)
            return;
        var index = Chosen is null ? -1 : Results.IndexOf(Chosen);
        Chosen = Results[Math.Clamp(index + step, 0, Results.Count - 1)];
    }

    public void OpenChosen()
    {
        if (Chosen is not { } hit)
            return;
        Hide();
        open(hit);
    }
}
