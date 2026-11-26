using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;

namespace Webignite.Desktop.ViewModels;

// Changes Claude or ChatGPT suggested to the client's details: shown before and after, and made only when accepted.
public partial class ClientDetailViewModel
{
    public ObservableCollection<SuggestedChangeRow> SuggestedChanges { get; } = [];
    public bool HasSuggestions => SuggestedChanges.Count > 0;
    public string SuggestionHeading { get; private set; } = "";

    private void LoadSuggestions()
    {
        SuggestedChanges.Clear();
        SuggestionHeading = "";
        if (!isNew && services.Clients.Get(client.Id) is { Suggested: { } waiting } stored)
        {
            var changes = ClientSuggestions.Changes(stored, waiting);
            foreach (var change in changes)
                SuggestedChanges.Add(new SuggestedChangeRow(change, this));
            SuggestionHeading = changes.Count > 0 ? ClientSuggestions.Heading(waiting, changes.Count) : "";
        }
        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(SuggestionHeading));
    }

    [RelayCommand]
    private void AcceptAll() => Accept(null);

    [RelayCommand]
    private void TurnDownAll()
    {
        services.Clients.TurnDown(client.Id);
        LoadSuggestions();
    }

    internal void Accept(IReadOnlyCollection<string>? fields)
    {
        var (saved, problems) = services.Clients.Accept(client.Id, fields);
        Problems.Clear();
        foreach (var problem in problems)
            Problems.Add(problem);
        OnPropertyChanged(nameof(HasProblems));
        if (saved is null)
        {
            EditingDetails = true;
            return;
        }
        if (!EditingDetails || !DetailsTyped())
            ShowDetails(saved);
        SavedStamp++;
        OnPropertyChanged(nameof(Title));
        Refresh();
    }

    internal void TurnDown(string field)
    {
        services.Clients.TurnDown(client.Id, [field]);
        LoadSuggestions();
    }
}

/// <summary>One suggested change on the client's page: the detail, what it says now and what it would say.</summary>
public sealed partial class SuggestedChangeRow(ClientChange change, ClientDetailViewModel owner)
{
    public string Label => change.Label;
    public string Before => change.Before.Length == 0 ? "(empty)" : change.Before;
    public string After => change.After.Length == 0 ? "(cleared)" : change.After;

    [RelayCommand]
    private void Accept() => owner.Accept([change.Field]);

    [RelayCommand]
    private void TurnDown() => owner.TurnDown(change.Field);
}
