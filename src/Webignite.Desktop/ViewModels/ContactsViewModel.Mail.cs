using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Mail;
using Contact = Webignite.Core.Mail.Contact;

namespace Webignite.Desktop.ViewModels;

// Contacts and mail: people Eddie writes to often who aren't in Contacts yet, and, for the person open, their
// latest mail and their client's quotes and invoices.
public sealed partial class ContactsViewModel
{
    public ObservableCollection<Contact> MailedOften { get; } = [];
    public bool HasMailedOften => MailedOften.Count > 0;

    public ObservableCollection<ClientMailRow> TheirMail { get; } = [];
    public ObservableCollection<DocumentRow> TheirDocuments { get; } = [];
    public bool HasTheirMail => TheirMail.Count > 0;
    public bool HasTheirDocuments => TheirDocuments.Count > 0;

    private int suggesting;
    private int reading;

    /// <summary>Done once the people mailed often are worked out, for the screenshot tool.</summary>
    public Task MailedOftenLoaded { get; private set; } = Task.CompletedTask;

    /// <summary>Done once the open person's mail is read.</summary>
    public Task TheirMailLoaded { get; private set; } = Task.CompletedTask;

    private void SuggestFromMail()
    {
        MailedOftenLoaded = SuggestFromMailAsync(++suggesting);
        MailedOftenLoaded.Forget("Finding people mailed often");
    }

    // The address book reads cached mail off the screen's thread.
    private async Task SuggestFromMailAsync(int run)
    {
        var book = await services.AddressBookAsync();
        if (run != suggesting)
            return;
        MailedOften.Clear();
        foreach (var contact in ContactRules.MailedOften(book, services.Contacts.List()))
            MailedOften.Add(contact);
        OnPropertyChanged(nameof(HasMailedOften));
    }

    /// <summary>Adds someone mailed often as a person, with their client when the address is a client's, and opens them.</summary>
    [RelayCommand]
    private void AddMailedOften(Contact contact)
    {
        if (!Keep())
            return;
        var client = ClientMail.For(services.Clients.List(), contact.Address);
        var (saved, _) = services.Contacts.Save(ContactRules.FromSender(contact.Name, contact.Address, client, Person.NewId(), DateTimeOffset.UtcNow));
        if (saved is not null)
            Show(saved);
    }

    private void LoadActivity(Person person)
    {
        TheirDocuments.Clear();
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (person.ClientId is { } clientId && services.Clients.Get(clientId) is { } client)
        {
            var summary = ClientDirectory.Summarise(client, services.Documents.List(), [], [], today);
            foreach (var doc in summary.Documents.Take(6))
                TheirDocuments.Add(new DocumentRow(doc, today));
        }
        OnPropertyChanged(nameof(HasTheirDocuments));
        TheirMail.Clear();
        OnPropertyChanged(nameof(HasTheirMail));
        TheirMailLoaded = person.Emails.Count == 0 ? Task.CompletedTask : LoadTheirMail(person, ++reading);
        TheirMailLoaded.Forget("Reading mail with a person");
    }

    // Finding their mail means unlocking cached mail, so it's read off the screen's thread.
    private async Task LoadTheirMail(Person person, int run)
    {
        var accounts = services.Store.ListMailAccounts();
        var found = await Task.Run(() => services.Store.FindMail(accounts.Select(a => a.Id), m => ContactRules.InvolvedIn(person, m), limit: 8));
        if (run != reading || open?.Id != person.Id)
            return;
        foreach (var mail in found)
            TheirMail.Add(new ClientMailRow(mail, accounts.First(a => a.Id == mail.AccountId), services.Store));
        OnPropertyChanged(nameof(HasTheirMail));
    }

    [RelayCommand]
    private void OpenTheirMail(ClientMailRow row) => services.Go.Mail(row.Summary);

    [RelayCommand]
    private void OpenTheirDocument(DocumentRow row) => services.Go.Document(row.Stored);
}
