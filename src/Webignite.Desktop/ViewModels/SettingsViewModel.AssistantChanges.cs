using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Desktop.ViewModels;

// Settings, Claude and ChatGPT: what they changed lately, newest first, with Put back for writing they replaced.
public partial class SettingsViewModel
{
    public const int ChangesShown = 40;

    public ObservableCollection<AssistantChangeRow> AssistantChanges { get; } = [];

    public bool HasAssistantChanges => AssistantChanges.Count > 0;

    [ObservableProperty] public partial string AssistantChangesMessage { get; private set; } = "";

    private void LoadAssistantChanges()
    {
        AssistantChanges.Clear();
        foreach (var entry in AssistantLog.List(services.Store).Take(ChangesShown))
            AssistantChanges.Add(new AssistantChangeRow(entry, this));
        OnPropertyChanged(nameof(HasAssistantChanges));
    }

    internal void PutBack(AssistantLog.Entry entry)
    {
        var problem = new AssistantPutBack(services.Store, services.Projects, services.Clients, services.Contacts, services.Notes).Apply(entry);
        AssistantChangesMessage = problem ?? "Put back.";
        LoadAssistantChanges();
    }
}

/// <summary>One thing Claude or ChatGPT changed.</summary>
public sealed partial class AssistantChangeRow(AssistantLog.Entry entry, SettingsViewModel page) : ObservableObject
{
    public string What => entry.What;
    public string When => $"{entry.Who}, {entry.At.ToLocalTime():d MMM HH:mm}";
    public bool CanPutBack => entry.CanPutBack;
    public bool WasPutBack => entry.PutBack;

    [RelayCommand]
    private void PutBack() => page.PutBack(entry);
}
