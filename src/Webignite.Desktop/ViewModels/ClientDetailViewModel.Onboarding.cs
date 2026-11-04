using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

// The new-site checklist on a client's page: ticked off as things come in, saved straight away.
public partial class ClientDetailViewModel
{
    public ObservableCollection<OnboardingRow> OnboardingItems { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddOnboardingItemCommand))]
    public partial string NewOnboardingItem { get; set; } = "";

    [ObservableProperty] public partial string OnboardingMessage { get; set; } = "";

    public bool HasOnboarding => client.Onboarding.Count > 0;
    public bool CanStartOnboarding => IsSaved && !HasOnboarding;
    public bool OnboardingOutstanding => Onboarding.InProgress(client);
    public string OnboardingProgress => Onboarding.Progress(client) + (client.OnboardingStarted is { } started ? $", started {Onboarding.Since(started)}" : "");

    private void LoadOnboarding()
    {
        OnboardingItems.Clear();
        foreach (var item in client.Onboarding)
            OnboardingItems.Add(new OnboardingRow(item, this));
        OnPropertyChanged(nameof(HasOnboarding));
        OnPropertyChanged(nameof(CanStartOnboarding));
        OnPropertyChanged(nameof(OnboardingOutstanding));
        OnPropertyChanged(nameof(OnboardingProgress));
    }

    // Only the checklist is changed here, on the client as stored now; unsaved typing in the details stays typed.
    private void SaveOnboarding(Func<Client, Client> change)
    {
        client = services.Clients.Change(client.Id, c =>
        {
            var changed = change(c);
            return c with { Onboarding = changed.Onboarding, OnboardingStarted = changed.OnboardingStarted };
        }) ?? client;
        LoadOnboarding();
    }

    [RelayCommand]
    private void StartOnboarding() => SaveOnboarding(c => Onboarding.Start(c, Today));

    internal void Tick(string name, bool done) => SaveOnboarding(c => Onboarding.Tick(c, name, done, Today));

    internal void RemoveOnboardingItem(string name) => SaveOnboarding(c => Onboarding.Remove(c, name));

    private bool CanAddOnboardingItem() => !string.IsNullOrWhiteSpace(NewOnboardingItem);

    [RelayCommand(CanExecute = nameof(CanAddOnboardingItem))]
    private void AddOnboardingItem()
    {
        SaveOnboarding(c => Onboarding.Add(c, NewOnboardingItem));
        NewOnboardingItem = "";
    }

    [RelayCommand]
    private void ClearOnboarding() => SaveOnboarding(c => c with { Onboarding = [], OnboardingStarted = null });

    /// <summary>Opens a mail to the client listing what's still needed. Nothing is sent until Send is clicked.</summary>
    [RelayCommand]
    private void DraftOnboardingReminder()
    {
        if (services.Drafts.EverydayMailbox is not { } mailbox)
        {
            OnboardingMessage = MailDrafts.NoMailbox;
            return;
        }
        var (subject, body) = Onboarding.Reminder(client, services.Settings.Xero.TenantName ?? "Webignite");
        var opened = services.Drafts.Open(new MailDraft(mailbox, client.Email, subject, body)
        {
            NoAddress = "This client has no email address. Type it in To.",
        });
        OnboardingMessage = opened ? "" : MailDrafts.AnotherOpen;
    }
}

public sealed partial class OnboardingRow(OnboardingItem item, ClientDetailViewModel owner) : ViewModelBase
{
    public string Name => item.Name;
    public string When => item.Received?.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public bool Done
    {
        get => item.Done;
        set
        {
            if (value != item.Done)
                owner.Tick(item.Name, value);
        }
    }

    [RelayCommand]
    private void Remove() => owner.RemoveOnboardingItem(item.Name);
}
