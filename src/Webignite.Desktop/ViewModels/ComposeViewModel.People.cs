using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>The Contacts button by To: everyone in Contacts with an address, and groups, to add to To, Cc or Bcc.</summary>
public partial class ComposeViewModel
{
    public ObservableCollection<PickPersonRow> PeopleToPick { get; } = [];

    [ObservableProperty] public partial string PeopleSearch { get; set; } = "";

    public bool NoPeopleToPick => PeopleToPick.Count == 0;

    partial void OnPeopleSearchChanged(string value) => ShowPeople();

    /// <summary>Called as the list opens, so it shows Contacts as they are now.</summary>
    public void OpenPeople()
    {
        PeopleSearch = "";
        ShowPeople();
    }

    private void ShowPeople()
    {
        var clients = services.Clients.List().ToDictionary(c => c.Id, c => c.Name);
        PeopleToPick.Clear();
        foreach (var contact in AddressBook.Pick(services.Contacts.List(), clients, PeopleSearch).Take(200))
            PeopleToPick.Add(new PickPersonRow(contact, this));
        OnPropertyChanged(nameof(NoPeopleToPick));
    }

    internal void AddPeople(Contact contact, AddressField field)
    {
        IEnumerable<MailPerson> people = contact.Members ?? [contact.Person];
        switch (field)
        {
            case AddressField.Cc:
                Cc = AddressBook.Add(Cc, people);
                ShowCc = true;
                break;
            case AddressField.Bcc:
                Bcc = AddressBook.Add(Bcc, people);
                ShowCc = true;
                break;
            default:
                To = AddressBook.Add(To, people);
                break;
        }
    }
}

/// <summary>A person or group in the Contacts list of a mail, with buttons for where they go.</summary>
public sealed partial class PickPersonRow(Contact contact, ComposeViewModel page)
{
    public string Name => contact.Name is { Length: > 0 } name ? name : contact.Address;
    public string Detail => contact.Members is { } members ? $"Group of {members.Count}" : contact.Address;

    [RelayCommand]
    private void AddTo() => page.AddPeople(contact, AddressField.To);

    [RelayCommand]
    private void AddCc() => page.AddPeople(contact, AddressField.Cc);

    [RelayCommand]
    private void AddBcc() => page.AddPeople(contact, AddressField.Bcc);
}
