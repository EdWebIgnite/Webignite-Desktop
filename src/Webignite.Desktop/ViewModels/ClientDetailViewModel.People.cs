using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

// The People card on a client's page: everyone in Contacts with this client, who the contact person is, and who
// gets quotes and invoices. Their name, number and address show in the details above.
public partial class ClientDetailViewModel
{
    public ObservableCollection<ClientPersonRow> People { get; } = [];
    public bool HasPeople => People.Count > 0;
    public bool NoPeople => People.Count == 0;

    /// <summary>People in Contacts with no client, to add to this one.</summary>
    public ObservableCollection<Person> Unlinked { get; } = [];
    public bool HasUnlinked => Unlinked.Count > 0;

    private void LoadPeople()
    {
        People.Clear();
        Unlinked.Clear();
        if (isNew)
            return;
        foreach (var person in services.Contacts.ForClient(client.Id))
            People.Add(new ClientPersonRow(person, client.MainContactId == person.Id, client.BillingContactId == person.Id, this));
        foreach (var person in services.Contacts.List().Where(p => p.ClientId is null))
            Unlinked.Add(person);
        OnPropertyChanged(nameof(HasPeople));
        OnPropertyChanged(nameof(NoPeople));
        OnPropertyChanged(nameof(HasUnlinked));
    }

    [RelayCommand]
    private void AddPerson()
    {
        if (TrySave())
            services.Go.NewPerson(client.Id);
    }

    [RelayCommand]
    private void LinkPerson(Person person)
    {
        services.Contacts.Link(person.Id, client.Id);
        Reload();
    }

    internal void MakeMain(Person person) => Point(() => services.Contacts.MakeMain(client.Id, person.Id));

    internal void MakeBilling(Person person) => Point(() => services.Contacts.MakeBilling(client.Id, person.Id));

    internal void Unlink(Person person)
    {
        services.Contacts.Link(person.Id, null);
        Reload();
    }

    internal void OpenPerson(Person person)
    {
        if (TrySave())
            services.Go.Person(person.Id);
    }

    internal void WriteTo(Person person)
    {
        if (person.Email is { } email)
            services.Go.WriteMail([new MailPerson(person.Name, email)]);
    }

    // Who the contact person is changes the details above, so what was typed there is saved first.
    private void Point(Func<Client?> change)
    {
        if (!TrySave())
            return;
        if (change() is { } changed)
            ShowDetails(changed);
        LoadPeople();
    }

    private void Reload()
    {
        if (services.Clients.Get(client.Id) is { } fresh)
            ShowDetails(fresh);
        LoadPeople();
    }
}

/// <summary>A person with the client, and what they are to it.</summary>
public sealed partial class ClientPersonRow(Person person, bool isMain, bool getsInvoices, ClientDetailViewModel page) : ObservableObject
{
    public Person Person { get; } = person;
    public string Name => Person.Name;
    public string Detail => string.Join(" · ", new[] { Person.Role, Person.Email, Person.Phone }.Where(t => !string.IsNullOrWhiteSpace(t)));
    public bool IsMain { get; } = isMain;
    public bool GetsInvoices { get; } = getsInvoices;
    public bool NotMain => !IsMain;
    public bool CanGetInvoices => !GetsInvoices && Person.Email is not null;
    public bool HasEmail => Person.Email is not null;

    [RelayCommand]
    private void Open() => page.OpenPerson(Person);

    [RelayCommand]
    private void MakeMain() => page.MakeMain(Person);

    [RelayCommand]
    private void MakeBilling() => page.MakeBilling(Person);

    [RelayCommand]
    private void Unlink() => page.Unlink(Person);

    [RelayCommand]
    private void Write() => page.WriteTo(Person);
}
