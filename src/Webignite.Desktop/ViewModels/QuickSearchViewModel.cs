using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Search;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Ctrl+K: one box that finds clients, quotes, invoices, mail, to-dos, notes, site logins, files and renewals.
/// Files are found by name, by tag, and (once typing stops) by the words inside them.
/// </summary>
public sealed partial class QuickSearchViewModel(AppServices services, Action<SearchHit> open) : ViewModelBase
{
    private const int WaitBeforeLookingInside = 400;
    private const int ShortestToLookInside = 3;

    // Read when the box opens; mail and file names join when they're in. Each is kept apart, so one arriving
    // late never pushes out another.
    private SearchSources opened = new();
    private IReadOnlyList<Webignite.Core.Mail.MailSummary> mail = [];
    private IReadOnlyList<string> files = [];
    private Dictionary<string, string> tagWords = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> insideWords = new(StringComparer.OrdinalIgnoreCase);
    private int opening; // counts up per opening, so a read finishing after the box closed or opened again is dropped
    private CancellationTokenSource? lookingInside;

    public ObservableCollection<SearchHit> Results { get; } = [];

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial SearchHit? Chosen { get; set; }

    public bool NothingFound => Query.Trim().Length > 0 && Results.Count == 0;
    public bool ShowHint => Query.Trim().Length == 0;

    [RelayCommand]
    public void Show()
    {
        var at = ++opening;
        opened = new SearchSources
        {
            Clients = services.Clients.List(),
            Documents = services.Documents.List(),
            Tasks = services.Store.ListTasks(),
            Renewals = services.Store.ListRenewals(),
            Notes = services.Notes.List(),
            Logins = services.Clients.AllLogins(),
            People = services.Contacts.List(),
        };
        mail = [];
        files = [];
        insideWords = new(StringComparer.OrdinalIgnoreCase);
        tagWords = TagWords();
        Query = "";
        Results.Clear();
        IsOpen = true;
        Refresh();
        // Mail has to be unlocked message by message, so it's read off the screen's thread; the box opens straight away.
        var accounts = services.Store.ListMailAccounts().Select(a => a.Id).ToList();
        MailLoaded = LoadMail(accounts, at);
        MailLoaded.Forget("Reading mail for search");
        FilesLoaded = LoadFiles(Files.FileOps.ClientRoot(services.Settings.Files), services.Settings.FileRecents, at);
        FilesLoaded.Forget("Reading file names for search");
    }

    /// <summary>Done once the mail has joined the search, e.g. for the screenshot tool to wait on.</summary>
    public Task MailLoaded { get; private set; } = Task.CompletedTask;

    /// <summary>Done once file names have joined the search.</summary>
    public Task FilesLoaded { get; private set; } = Task.CompletedTask;

    /// <summary>Done once the words inside files have been looked through for what's typed now.</summary>
    public Task InsideLoaded { get; private set; } = Task.CompletedTask;

    // Each tagged file, with its tags' names, so "needs client ok" finds the files tagged with it.
    private Dictionary<string, string> TagWords()
    {
        var words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in services.FileTags.Tags)
            foreach (var path in services.FileTags.With(tag.Id))
                words[path] = words.TryGetValue(path, out var more) ? more + " · " + tag.Name : tag.Name;
        return words;
    }

    private async Task LoadMail(IReadOnlyList<string> accounts, int at)
    {
        var found = await Task.Run(() => services.Store.FindMail(accounts, _ => true, 1500));
        if (!IsOpen || at != opening)
            return;
        mail = found;
        Refresh();
    }

    private async Task LoadFiles(string? root, IReadOnlyList<string> recents, int at)
    {
        var found = await Files.FileIndex.ReadAsync(root, recents);
        if (!IsOpen || at != opening)
            return;
        files = found;
        Refresh();
    }

    // The file names, tags, mail and words are let go when the box closes; they're read again next time.
    public void Hide()
    {
        IsOpen = false;
        opening++;
        lookingInside?.Cancel();
        lookingInside = null;
        opened = new SearchSources();
        mail = [];
        files = [];
        tagWords = new(StringComparer.OrdinalIgnoreCase);
        insideWords = new(StringComparer.OrdinalIgnoreCase);
        Results.Clear();
    }

    partial void OnQueryChanged(string value)
    {
        insideWords = new(StringComparer.OrdinalIgnoreCase);
        Refresh();
        LookInside(value);
    }

    // Once typing stops, the client folders are looked through for files with these words inside (text, Word, PDF).
    private void LookInside(string query)
    {
        lookingInside?.Cancel();
        lookingInside = null;
        var root = Files.FileOps.ClientRoot(services.Settings.Files);
        if (!IsOpen || root is null || query.Trim().Length < ShortestToLookInside)
        {
            InsideLoaded = Task.CompletedTask;
            return;
        }
        var stop = new CancellationTokenSource();
        lookingInside = stop;
        InsideLoaded = LookInsideAsync(root, query, stop.Token);
        InsideLoaded.Forget("Looking inside files for search");
    }

    private async Task LookInsideAsync(string root, string query, CancellationToken stop)
    {
        try
        {
            await Task.Delay(WaitBeforeLookingInside, stop);
            await Files.FileSearcher.Run(root, query, inside: true, found: hits =>
            {
                if (stop.IsCancellationRequested)
                    return;
                foreach (var hit in hits.Where(h => h.Snippet is not null))
                    insideWords[hit.Entry.Path] = hit.Snippet!;
                Refresh();
            }, lookedAt: _ => { }, stop);
        }
        catch (OperationCanceledException)
        {
            // Typing carried on, or the box closed.
        }
    }

    private SearchSources Sources()
    {
        var words = new Dictionary<string, string>(tagWords, StringComparer.OrdinalIgnoreCase);
        foreach (var (path, snippet) in insideWords)
            words[path] = words.TryGetValue(path, out var tags) ? tags + " · " + snippet : snippet;
        return opened with { Mail = mail, Files = files, FileWords = words };
    }

    private void Refresh()
    {
        // The one picked stays picked as more results come in, so arrowing down isn't undone.
        var picked = Chosen is { } c ? (c.Kind, c.Title, c.FilePath) : default;
        Results.Clear();
        foreach (var hit in QuickSearch.Find(Query, Sources()))
            Results.Add(hit);
        Chosen = Results.FirstOrDefault(h => (h.Kind, h.Title, h.FilePath) == picked) ?? Results.FirstOrDefault();
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
