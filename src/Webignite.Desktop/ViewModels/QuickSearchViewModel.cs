using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Search;

namespace Webignite.Desktop.ViewModels;

/// <summary>Ctrl+K: one box that finds clients, quotes, invoices, mail, to-dos and renewals.</summary>
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
        // Read once per opening, so typing stays quick.
        var accounts = services.Store.ListMailAccounts().Select(a => a.Id).ToList();
        sources = new SearchSources
        {
            Clients = services.Store.ListClients(),
            Documents = services.Store.List(),
            Mail = services.Store.FindMail(accounts, _ => true, 1500),
            Tasks = services.Store.ListTasks(),
            Renewals = services.Store.ListRenewals(),
        };
        Query = "";
        Results.Clear();
        IsOpen = true;
        Refresh();
    }

    [RelayCommand]
    public void Hide() => IsOpen = false;

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

    [RelayCommand]
    public void OpenChosen()
    {
        if (Chosen is not { } hit)
            return;
        IsOpen = false;
        open(hit);
    }

    [RelayCommand]
    private void OpenHit(SearchHit hit)
    {
        Chosen = hit;
        OpenChosen();
    }
}
