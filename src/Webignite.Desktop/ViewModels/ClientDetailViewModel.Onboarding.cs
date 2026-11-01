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

    private void SaveOnboarding(Client changed)
    {
        // Keep any unsaved typing in the details; only the checklist is saved here.
        var stored = services.Store.ListClients().FirstOrDefault(c => c.Id == client.Id) ?? client;
        client = services.Store.SaveClient(stored with { Onboarding = changed.Onboarding, OnboardingStarted = changed.OnboardingStarted });
        LoadOnboarding();
    }

    [RelayCommand]
    private void StartOnboarding() => SaveOnboarding(Onboarding.Start(client, Today));

    internal void Tick(string name, bool done) => SaveOnboarding(Onboarding.Tick(client, name, done, Today));

    internal void RemoveOnboardingItem(string name) => SaveOnboarding(Onboarding.Remove(client, name));

    private bool CanAddOnboardingItem() => !string.IsNullOrWhiteSpace(NewOnboardingItem);

    [RelayCommand(CanExecute = nameof(CanAddOnboardingItem))]
    private void AddOnboardingItem()
    {
        SaveOnboarding(Onboarding.Add(client, NewOnboardingItem));
        NewOnboardingItem = "";
    }

    [RelayCommand]
    private void ClearOnboarding() => SaveOnboarding(client with { Onboarding = [], OnboardingStarted = null });

    /// <summary>Opens a mail to the client listing what's still needed. Nothing is sent until Send is clicked.</summary>
    [RelayCommand]
    private void DraftOnboardingReminder()
    {
        var accounts = services.Store.ListMailAccounts();
        var mailbox = accounts.FirstOrDefault(a => !a.SendsInvoices) ?? accounts.FirstOrDefault();
        if (mailbox is null)
        {
            OnboardingMessage = "Add a mailbox under Mail first.";
            return;
        }
        var (subject, body) = Onboarding.Reminder(client, services.XeroSettings.TenantName ?? "Webignite");
        var to = Recipients.Parse(client.Email);
        var mail = new OutgoingMail
        {
            AccountId = mailbox.Id,
            To = to.People,
            Subject = subject,
            Body = body + Replies.StartBody(mailbox.Signature),
        };
        var opened = services.ComposeMail?.Invoke(mail, to.People.Count == 0 ? "This client has no email address. Type it in To." : null, null) ?? false;
        OnboardingMessage = opened ? "" : "Another mail is open in Mail with changes. Send or close it first.";
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
