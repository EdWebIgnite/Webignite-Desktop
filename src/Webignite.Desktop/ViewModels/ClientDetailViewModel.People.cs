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
            People.Add(new ClientPersonRow(person, client, this));
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

    internal void Send(Person person, MailFor kind) => Point(() => services.Contacts.Send(client.Id, person.Id, kind));

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
public sealed partial class ClientPersonRow(Person person, Client client, ClientDetailViewModel page) : ObservableObject
{
    private bool Has(MailFor kind) => ClientMailTo.ContactId(client, kind) == Person.Id;
    private bool Can(MailFor kind) => Person.Email is not null && !Has(kind);

    public Person Person { get; } = person;
    public string Name => Person.Name;
    public string Detail => string.Join(" · ", new[] { Person.Role, Person.Email, Person.Phone }.Where(t => !string.IsNullOrWhiteSpace(t)));
    public bool IsMain { get; } = client.MainContactId == person.Id;
    public bool NotMain => !IsMain;

    /// <summary>"Gets quotes, invoices": which mail goes to them.</summary>
    public string Gets => ClientMailTo.Gets(client, Person.Id) is { Length: > 0 } what ? "Gets " + what : "";
    public bool GetsSomething => Gets.Length > 0;
    public bool CanGetQuotes => Can(MailFor.Quotes);
    public bool CanGetInvoices => Can(MailFor.Invoices);
    public bool CanGetStatements => Can(MailFor.Statements);
    public bool CanGetProjects => Can(MailFor.Projects);
    public bool HasEmail => Person.Email is not null;

    [RelayCommand]
    private void Open() => page.OpenPerson(Person);

    [RelayCommand]
    private void MakeMain() => page.MakeMain(Person);

    [RelayCommand]
    private void MakeBilling() => page.Send(Person, MailFor.Invoices);

    [RelayCommand]
    private void SendQuotes() => page.Send(Person, MailFor.Quotes);

    [RelayCommand]
    private void SendStatements() => page.Send(Person, MailFor.Statements);

    [RelayCommand]
    private void SendProjects() => page.Send(Person, MailFor.Projects);

    [RelayCommand]
    private void Unlink() => page.Unlink(Person);

    [RelayCommand]
    private void Write() => page.WriteTo(Person);
}
