using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The Contacts page: everyone, searchable and by group, and one person open for changing. Someone can be with a
/// client; on the client's page they can be its contact person or the one who gets quotes and invoices.
/// </summary>
public sealed partial class ContactsViewModel : ViewModelBase, IRefreshable, IHoldsWork, ITabTitle, IHasPanes, IDisposable
{
    public const string AllGroups = "Everyone";

    private readonly AppServices services;
    private List<ContactRow> all = [];
    private Person? open;

    public ContactsViewModel(AppServices services)
    {
        this.services = services;
        Panels = new PanelsViewModel(services.Settings, "contacts", [("list", "People"), ("person", "Open person")]);
        services.Contacts.Changed += Refresh;
        services.Clients.PeopleChanged += Refresh;
        Refresh();
    }

    public PanelsViewModel Panels { get; }

    public string? TabTitle => IsOpen ? (EditName.Length > 0 ? EditName : "New person") : null;

    public bool HoldsWork => IsOpen;

    public ObservableCollection<ContactRow> Rows { get; } = [];
    public ObservableCollection<string> Groups { get; } = [];

    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial string Group { get; private set; } = AllGroups;
    [ObservableProperty] public partial string Headline { get; private set; } = "";

    public bool IsEmpty => all.Count == 0;
    public bool NoMatches => all.Count > 0 && Rows.Count == 0;

    partial void OnSearchChanged(string value) => Filter();

    /// <summary>The group picked in the list; the list is one-way, so only a real pick counts.</summary>
    public void PickGroup(string group)
    {
        Group = group;
        Filter();
    }

    public void Refresh()
    {
        var clients = services.Clients.List().ToDictionary(c => c.Id);
        var people = services.Contacts.List();
        all = [.. people.Select(p => new ContactRow(p, p.ClientId is { } id && clients.TryGetValue(id, out var c) ? c : null))];
        Groups.Clear();
        Groups.Add(AllGroups);
        foreach (var group in ContactRules.AllGroups(people))
            Groups.Add(group);
        if (!Groups.Contains(Group))
            Group = AllGroups;
        else
            OnPropertyChanged(nameof(Group)); // the list was rebuilt, so the drop-down needs its pick again
        ClientChoices = ClientPicker.Choices("No client", clients.Values.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase));
        if (IsOpen)
            EditClient = ClientChoices.FirstOrDefault(c => c.Value?.Id == EditClient?.Value?.Id) ?? ClientChoices.FirstOrDefault();
        Headline = people.Count switch
        {
            0 => "",
            1 => "1 person",
            _ => $"{people.Count} people, {people.Count(p => p.ClientId is not null)} with clients",
        };
        OnPropertyChanged(nameof(IsEmpty));
        Filter();
        SuggestFromMail();
    }

    private void Filter()
    {
        Rows.Clear();
        foreach (var row in all.Where(r => (Group == AllGroups || r.Person.Groups.Contains(Group, StringComparer.CurrentCultureIgnoreCase))
                     && ContactRules.Matches(r.Person, Search, r.Client?.Name)))
        {
            row.IsOpen = row.Person.Id == open?.Id;
            Rows.Add(row);
        }
        OnPropertyChanged(nameof(NoMatches));
    }

    // The person open on the right, as fields to change.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClosed), nameof(TabTitle), nameof(HoldsWork))]
    public partial bool IsOpen { get; private set; }

    public bool IsClosed => !IsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    public partial string EditName { get; set; } = "";

    [ObservableProperty] public partial string EditRole { get; set; } = "";
    [ObservableProperty] public partial string EditCompany { get; set; } = "";
    [ObservableProperty] public partial string EditGroups { get; set; } = "";
    [ObservableProperty] public partial string EditNotes { get; set; } = "";
    [ObservableProperty] public partial Choice<Client?>? EditClient { get; private set; }

    /// <summary>The client picked in the list; the list is one-way, so only a real pick counts.</summary>
    public void PickClient(Choice<Client?> choice) => EditClient = choice;
    [ObservableProperty] public partial IReadOnlyList<Choice<Client?>> ClientChoices { get; private set; } = [];
    [ObservableProperty] public partial string Message { get; private set; } = "";

    public ObservableCollection<WayRow> Emails { get; } = [];
    public ObservableCollection<WayRow> Phones { get; } = [];
    public ObservableCollection<string> Problems { get; } = [];
    public bool HasProblems => Problems.Count > 0;
    public bool IsSaved => open is not null && services.Contacts.Get(open.Id) is not null;
    public bool HasEmail => open?.Email is not null;
    public bool HasPhone => open?.Phone is not null;
    public bool HasClient => open?.ClientId is { } id && services.Clients.Get(id) is not null;

    /// <summary>Set by the page: copies text.</summary>
    public Func<string, Task>? CopyText { get; set; }

    [RelayCommand]
    private void Open(ContactRow row)
    {
        if (!Keep())
            return;
        Show(row.Person);
    }

    /// <summary>Opens a person by id, e.g. from a client's page or Ctrl+K.</summary>
    public void OpenPerson(string id)
    {
        if (services.Contacts.Get(id) is { } person && Keep())
            Show(person);
    }

    /// <summary>A new person, with a client already picked when started from a client's page.</summary>
    [RelayCommand]
    public void New(string? clientId = null)
    {
        if (!Keep())
            return;
        var client = clientId is null ? null : services.Clients.Get(clientId);
        Show(new Person { Id = Person.NewId(), Name = "", ClientId = clientId, Company = client?.Name });
    }

    private void Show(Person person)
    {
        open = person;
        EditName = person.Name;
        EditRole = person.Role ?? "";
        EditCompany = person.Company ?? "";
        EditGroups = string.Join(", ", person.Groups);
        EditNotes = person.Notes ?? "";
        EditClient = ClientChoices.FirstOrDefault(c => c.Value?.Id == person.ClientId) ?? ClientChoices.FirstOrDefault();
        Emails.Clear();
        foreach (var email in person.Emails)
            Emails.Add(new WayRow(email.Label, email.Value, this, isEmail: true));
        if (Emails.Count == 0)
            Emails.Add(new WayRow("Work", "", this, isEmail: true));
        Phones.Clear();
        foreach (var phone in person.Phones)
            Phones.Add(new WayRow(phone.Label, phone.Value, this, isEmail: false));
        if (Phones.Count == 0)
            Phones.Add(new WayRow("Mobile", "", this, isEmail: false));
        Problems.Clear();
        OnPropertyChanged(nameof(HasProblems));
        Message = "";
        IsOpen = true;
        Changed();
        LoadActivity(person);
        foreach (var row in all)
            row.IsOpen = row.Person.Id == person.Id;
        if (!Panels["person"].IsShown)
            Panels["person"].IsShown = true;
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(IsSaved));
        OnPropertyChanged(nameof(HasEmail));
        OnPropertyChanged(nameof(HasPhone));
        OnPropertyChanged(nameof(HasClient));
    }

    private Person Typed() => open! with
    {
        Name = EditName,
        Role = EditRole,
        Company = EditCompany,
        Notes = EditNotes,
        Groups = [EditGroups],
        ClientId = EditClient?.Value?.Id,
        Emails = [.. Emails.Select(e => new ContactWay(e.Label, e.Value))],
        Phones = [.. Phones.Select(p => new ContactWay(p.Label, p.Value))],
    };

    [RelayCommand]
    private void Save() => TrySave(quietly: false);

    /// <summary>Saves what's typed before something else opens or the app closes. False when it needs fixing first.</summary>
    public bool Keep()
    {
        if (!IsOpen || open is null)
            return true;
        // A new person with nothing typed is simply dropped.
        if (!IsSaved && string.IsNullOrWhiteSpace(EditName) && Emails.All(e => e.Value.Trim().Length == 0) && Phones.All(p => p.Value.Trim().Length == 0))
            return true;
        return TrySave(quietly: true);
    }

    private bool TrySave(bool quietly)
    {
        if (open is null)
            return true;
        var (saved, problems) = services.Contacts.Save(Typed());
        Problems.Clear();
        foreach (var problem in problems)
            Problems.Add(problem);
        OnPropertyChanged(nameof(HasProblems));
        if (saved is null)
            return false;
        open = saved;
        if (!quietly)
            Message = "Saved.";
        Changed();
        return true;
    }

    [RelayCommand]
    private void Close()
    {
        if (!Keep())
            return;
        open = null;
        IsOpen = false;
        foreach (var row in all)
            row.IsOpen = false;
    }

    [RelayCommand]
    private void Delete()
    {
        if (open is null)
            return;
        var gone = services.Contacts.Delete(open.Id);
        open = null;
        IsOpen = false;
        if (gone is not null)
            services.OfferUndo($"Deleted {gone.Name}", () => services.Contacts.Restore(gone));
    }

    [RelayCommand]
    private void AddEmail() => Emails.Add(new WayRow(Emails.Count == 0 ? "Work" : "Other", "", this, isEmail: true));

    [RelayCommand]
    private void AddPhone() => Phones.Add(new WayRow(Phones.Count == 0 ? "Mobile" : "Work", "", this, isEmail: false));

    internal void RemoveWay(WayRow row)
    {
        if (!Emails.Remove(row))
            Phones.Remove(row);
    }

    [RelayCommand]
    private void WriteMail()
    {
        if (TrySave(quietly: true) && open?.Email is { } email)
            services.Go.WriteMail([new MailPerson(open.Name, email)]);
    }

    [RelayCommand]
    private void OpenClient()
    {
        if (TrySave(quietly: true) && open?.ClientId is { } id && services.Clients.Get(id) is { } client)
            services.Go.Client(client);
    }

    [RelayCommand]
    private async Task CopyPhone()
    {
        if (open?.Phone is { } phone && CopyText is { } copy)
        {
            await copy(phone);
            Message = $"Copied {phone}.";
        }
    }

    [RelayCommand]
    private async Task CopyEmail()
    {
        if (open?.Email is { } email && CopyText is { } copy)
        {
            await copy(email);
            Message = $"Copied {email}.";
        }
    }

    public void Dispose()
    {
        services.Contacts.Changed -= Refresh;
        services.Clients.PeopleChanged -= Refresh;
    }
}

/// <summary>A person in the list: name, what they do and where, and how to reach them.</summary>
public sealed partial class ContactRow(Person person, Client? client) : ObservableObject
{
    public Person Person { get; } = person;
    public Client? Client { get; } = client;
    public string Name => Person.Name;
    public string About => string.Join(" · ", new[] { Person.Role, Client?.Name ?? Person.Company }.Where(t => !string.IsNullOrWhiteSpace(t)));
    public string Reach => string.Join(" · ", new[] { Person.Email, Person.Phone }.Where(t => !string.IsNullOrWhiteSpace(t)));
    public bool HasAbout => About.Length > 0;
    public bool HasEmail => Person.Email is not null;

    [ObservableProperty] public partial bool IsOpen { get; set; }
}

/// <summary>An email address or phone number being typed, with what it's for.</summary>
public sealed partial class WayRow(string label, string value, ContactsViewModel page, bool isEmail) : ObservableObject
{
    [ObservableProperty] public partial string Label { get; set; } = label;
    [ObservableProperty] public partial string Value { get; set; } = value;

    public IReadOnlyList<string> Labels { get; } = isEmail ? ContactRules.EmailLabels : ContactRules.PhoneLabels;
    public string Placeholder { get; } = isEmail ? "name@company.co.za" : "082 555 0100";

    [RelayCommand]
    private void Remove() => page.RemoveWay(this);
}
