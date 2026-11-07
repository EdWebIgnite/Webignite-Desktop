using System.Globalization;
using Webignite.Core.Assistants;
using Webignite.Core.Calendar;
using Webignite.Core.Clients;
using Webignite.Core.Notes;
using Webignite.Core.Renewals;
using Webignite.Core.Retainers;
using Webignite.Core.Time;
using Webignite.Core.Writing;
using Webignite.Data;

namespace Webignite.Services;

/// <summary>
/// What Claude and ChatGPT can do with the rest of the business, for Eddie: clients and their notes, people in
/// Contacts, notes, the calendar, time logged and renewals; retainers they can read. They go through the same
/// services as the app and never delete. Site logins and passwords are never read or shown here. A problem comes
/// back as an <see cref="ArgumentException"/> in plain words, and every change leaves a note so the open app
/// shows it.
/// </summary>
public sealed class AssistantBusiness
{
    private readonly LocalStore store;
    private readonly TimeProvider clock;
    private readonly ClientService clients;
    private readonly ContactService contacts;
    private readonly NoteService notes;

    public AssistantBusiness(LocalStore store, TimeProvider? clock = null)
    {
        this.store = store;
        this.clock = clock ?? TimeProvider.System;
        clients = new ClientService(store, this.clock);
        contacts = new ContactService(store, this.clock);
        notes = new NoteService(store, this.clock);
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private void Changed(string area) => AssistantSignal.Touch(store, area, clock);

    /// <summary>Which assistant is asking: Claude or ChatGPT.</summary>
    public string Who
    {
        get;
        set => field = value == "ChatGPT" ? "ChatGPT" : "Claude";
    } = "Claude";

    // A piece of writing (or a client's email or name) replaced, kept so Settings can put it back.
    private void KeepOld(string area, string recordId, string what, string field, string? before, string? beforeHtml = null) =>
        AssistantLog.Add(store, clock.GetUtcNow(), Who, area, recordId, what, field, before ?? "", beforeHtml);

    /// <summary>Writes a change in the list of what Claude and ChatGPT changed.</summary>
    public void Noted(string what) => AssistantLog.Add(store, clock.GetUtcNow(), Who, "", "", what);

    private static void Check(params string[] problems)
    {
        if (AssistantLimits.First(problems) is { Length: > 0 } problem)
            throw new ArgumentException(problem);
    }

    private const int Short = AssistantLimits.Title;
    private const int Long = AssistantLimits.Text;

    // Clients

    public sealed record ClientLine(string Id, string Name, string? ContactPerson, string? Email, string? Phone, string? Website, decimal? HourlyRate);

    public sealed record ClientNoteLine(string Date, string Text);

    public sealed record ClientDetail(ClientLine Client, string? Address, string? Notes, IReadOnlyList<ClientNoteLine> Log,
        IReadOnlyList<PersonLine> People, IReadOnlyList<string> NoteTitles, IReadOnlyList<RenewalLine> Renewals, IReadOnlyList<RetainerLine> Retainers);

    public IReadOnlyList<ClientLine> ListClients(string? search = null, int limit = 100) =>
        [.. clients.List()
            .Where(c => string.IsNullOrWhiteSpace(search) || Holds(search, c.Name, c.ContactPerson, c.Email, c.Website))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(Line)];

    public ClientDetail GetClient(string client)
    {
        var found = FindClient(client);
        var key = ClientDirectory.Key(found.Name);
        return new ClientDetail(
            Line(found),
            found.Address,
            found.Notes,
            [.. found.Log.Select(n => new ClientNoteLine(AssistantLookup.Day(n.Date)!, n.Text))],
            [.. contacts.ForClient(found.Id).Select(Line)],
            [.. notes.List().Where(n => n.Deleted is null && n.ClientId == found.Id).Select(n => NoteRules.Name(n))],
            [.. store.ListRenewals().Where(r => ClientDirectory.Key(r.ClientName ?? "") == key).Select(Line)],
            [.. store.ListRetainers().Where(r => ClientDirectory.Key(r.ClientName) == key).Select(Line)]);
    }

    /// <summary>A new client. If one with the same name is already there, that one is given back instead.</summary>
    public ClientDetail AddClient(string name, string? contactPerson = null, string? email = null, string? phone = null, string? website = null,
        string? address = null, string? notes = null, decimal? hourlyRate = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Give the client a name.");
        Check(AssistantLimits.TooLong(name, "the name", Short), AssistantLimits.TooLong(contactPerson, "the contact person", Short),
            AssistantLimits.TooLong(email, "the email", Short), AssistantLimits.TooLong(phone, "the phone number", Short),
            AssistantLimits.TooLong(website, "the website", Short), AssistantLimits.TooLong(address, "the address", 2000),
            AssistantLimits.TooLong(notes, "the notes", Long));
        if (clients.Find(name) is { } existing)
            return GetClient(existing.Id);
        var (saved, problems) = clients.SaveDetails(new Client
        {
            Id = Client.NewId(),
            Name = name.Trim(),
            ContactPerson = Blank(contactPerson),
            Email = Blank(email),
            Phone = Blank(phone),
            Website = Blank(website),
            Address = Blank(address),
            Notes = Blank(notes),
            HourlyRate = hourlyRate,
        });
        if (saved is null)
            throw new ArgumentException(string.Join(" ", problems));
        Changed("clients");
        return GetClient(saved.Id);
    }

    /// <summary>Changes a client's details. Only what's given changes; an empty value clears that detail.</summary>
    public ClientDetail UpdateClient(string client, string? name = null, string? contactPerson = null, string? email = null, string? phone = null,
        string? website = null, string? address = null, string? notes = null, decimal? hourlyRate = null)
    {
        var found = FindClient(client);
        Check(AssistantLimits.TooLong(name, "the name", Short), AssistantLimits.TooLong(contactPerson, "the contact person", Short),
            AssistantLimits.TooLong(email, "the email", Short), AssistantLimits.TooLong(phone, "the phone number", Short),
            AssistantLimits.TooLong(website, "the website", Short), AssistantLimits.TooLong(address, "the address", 2000),
            AssistantLimits.TooLong(notes, "the notes", Long), AssistantLimits.Emptying(found.Notes, notes, "a client's notes"),
            AssistantLimits.Emptying(found.Email, email, "a client's email"));
        var edited = found with
        {
            Name = string.IsNullOrWhiteSpace(name) ? found.Name : name.Trim(),
            ContactPerson = contactPerson is null ? found.ContactPerson : Blank(contactPerson),
            Email = email is null ? found.Email : Blank(email),
            Phone = phone is null ? found.Phone : Blank(phone),
            Website = website is null ? found.Website : Blank(website),
            Address = address is null ? found.Address : Blank(address),
            Notes = notes is null ? found.Notes : Blank(notes),
            HourlyRate = hourlyRate ?? found.HourlyRate,
        };
        var (saved, problems) = clients.SaveDetails(edited, found);
        if (saved is null)
            throw new ArgumentException(string.Join(" ", problems));
        if (notes is not null && !string.IsNullOrWhiteSpace(found.Notes) && found.Notes != saved.Notes)
            KeepOld("clients", saved.Id, $"Replaced the notes on {saved.Name}", "client-notes", found.Notes);
        if (!string.Equals(found.Email ?? "", saved.Email ?? "", StringComparison.OrdinalIgnoreCase))
            saved = Flag(saved, "client-email", found.Email, $"{Who} changed the email from {Shown(found.Email)} to {Shown(saved.Email)}.");
        if (found.Name != saved.Name)
            saved = Flag(saved, "client-name", found.Name, $"{Who} changed the name from {found.Name} to {saved.Name}.");
        Changed("clients");
        return GetClient(saved.Id);
    }

    private static string Shown(string? value) => string.IsNullOrWhiteSpace(value) ? "nothing" : value;

    // An email or name changed by an assistant: kept to put back, noted in the client's history, and marked on the
    // client so the email and Xero steps wait until Eddie says it's right.
    private Client Flag(Client client, string field, string? before, string what)
    {
        KeepOld("clients", client.Id, what, field, before);
        return clients.Change(client.Id, c => ClientRecords.AddNote(c, what, Today) with { Check = new ClientCheck(what, clock.GetUtcNow(), Who) }) ?? client;
    }

    /// <summary>A dated note in the client's history, e.g. what was agreed on a call.</summary>
    public ClientDetail AddClientNote(string client, string text, string from, string? date = null)
    {
        var found = FindClient(client);
        var (given, day, problem) = AssistantLookup.Date(date, "date");
        if (problem.Length > 0)
            throw new ArgumentException(problem);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Write the note first.");
        Check(AssistantLimits.TooLong(text, "the note", 3900));
        clients.Change(found.Id, c => ClientRecords.AddNote(c, $"{text.Trim()} ({from})", given && day is { } d ? d : Today));
        Changed("clients");
        return GetClient(found.Id);
    }

    // Contacts

    public sealed record PersonLine(string Id, string Name, string? Company, string? Role, IReadOnlyList<string> Emails, IReadOnlyList<string> Phones,
        string? Client, IReadOnlyList<string> Groups, string? Notes);

    public IReadOnlyList<PersonLine> ListContacts(string? search = null, string? client = null, int limit = 100)
    {
        var clientId = client is { Length: > 0 } ? FindClient(client).Id : null;
        return [.. contacts.List()
            .Where(p => clientId is null || p.ClientId == clientId)
            .Where(p => string.IsNullOrWhiteSpace(search)
                || Holds(search, [p.Name, p.Company, p.Role, .. p.Emails.Select(e => e.Value), .. p.Phones.Select(e => e.Value), .. p.Groups]))
            .Take(Math.Clamp(limit, 1, 500))
            .Select(Line)];
    }

    /// <summary>A new person in Contacts. Someone with the same email already there is given back instead.</summary>
    public PersonLine AddContact(string name, string? email = null, string? phone = null, string? company = null, string? role = null,
        string? client = null, string? notes = null, IReadOnlyList<string>? groups = null)
    {
        Check(AssistantLimits.TooLong(name, "the name", Short), AssistantLimits.TooLong(email, "the email", Short), AssistantLimits.TooLong(phone, "the phone number", Short),
            AssistantLimits.TooLong(company, "the company", Short), AssistantLimits.TooLong(role, "the role", Short), AssistantLimits.TooLong(notes, "the notes", Long),
            (groups?.Count ?? 0) > 20 ? "Give at most 20 groups." : "");
        if (email is { Length: > 0 } && contacts.List().FirstOrDefault(p => p.Emails.Any(e => string.Equals(e.Value, email.Trim(), StringComparison.OrdinalIgnoreCase))) is { } known)
            return Line(known);
        var (saved, problems) = contacts.Save(new Person
        {
            Id = Person.NewId(),
            Name = (name ?? "").Trim(),
            Company = Blank(company),
            Role = Blank(role),
            Notes = Blank(notes),
            Emails = email is { Length: > 0 } ? [new ContactWay("Work", email.Trim())] : [],
            Phones = phone is { Length: > 0 } ? [new ContactWay("Mobile", phone.Trim())] : [],
            ClientId = client is { Length: > 0 } ? FindClient(client).Id : null,
            Groups = groups ?? [],
        });
        if (saved is null)
            throw new ArgumentException(string.Join(" ", problems));
        Changed("contacts");
        return Line(saved);
    }

    /// <summary>
    /// Changes a person. Only what's given changes. A new email or number becomes their main one, keeping the
    /// others; client "none" unlinks them.
    /// </summary>
    public PersonLine UpdateContact(string contact, string? name = null, string? email = null, string? phone = null, string? company = null,
        string? role = null, string? client = null, string? notes = null, IReadOnlyList<string>? groups = null)
    {
        var found = FindPerson(contact);
        Check(AssistantLimits.TooLong(name, "the name", Short), AssistantLimits.TooLong(email, "the email", Short), AssistantLimits.TooLong(phone, "the phone number", Short),
            AssistantLimits.TooLong(company, "the company", Short), AssistantLimits.TooLong(role, "the role", Short), AssistantLimits.TooLong(notes, "the notes", Long),
            AssistantLimits.Emptying(found.Notes, notes, "a person's notes"), (groups?.Count ?? 0) > 20 ? "Give at most 20 groups." : "");
        var clientId = found.ClientId;
        if (client is { Length: > 0 })
            clientId = client.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : FindClient(client).Id;
        var (saved, problems) = contacts.Save(found with
        {
            Name = string.IsNullOrWhiteSpace(name) ? found.Name : name.Trim(),
            Company = company is null ? found.Company : Blank(company),
            Role = role is null ? found.Role : Blank(role),
            Notes = notes is null ? found.Notes : Blank(notes),
            Emails = email is { Length: > 0 } ? Main(found.Emails, email, "Work") : found.Emails,
            Phones = phone is { Length: > 0 } ? Main(found.Phones, phone, "Mobile") : found.Phones,
            ClientId = clientId,
            Groups = groups ?? found.Groups,
        });
        if (saved is null)
            throw new ArgumentException(string.Join(" ", problems));
        if (notes is not null && !string.IsNullOrWhiteSpace(found.Notes) && found.Notes != saved.Notes)
            KeepOld("contacts", saved.Id, $"Replaced the notes on {saved.Name}", "contact-notes", found.Notes);
        // The main email of a client's contact person, or of who gets their invoices, is the client's email too.
        if (email is { Length: > 0 } && !string.Equals(found.Email, saved.Email, StringComparison.OrdinalIgnoreCase) && saved.ClientId is { } owner
            && clients.Get(owner) is { } theirs && (theirs.MainContactId == saved.Id || theirs.BillingContactId == saved.Id))
        {
            var what = $"{Who} changed {saved.Name}'s email from {Shown(found.Email)} to {Shown(saved.Email)}, which {theirs.Name}'s email follows.";
            AssistantLog.Add(store, clock.GetUtcNow(), Who, "contacts", saved.Id, what);
            clients.Change(theirs.Id, c => ClientRecords.AddNote(c, what, Today) with { Check = new ClientCheck(what, clock.GetUtcNow(), Who) });
        }
        Changed("contacts");
        if (saved.ClientId is not null || found.ClientId is not null)
            Changed("clients");
        return Line(saved);
    }

    private static IReadOnlyList<ContactWay> Main(IReadOnlyList<ContactWay> ways, string value, string label)
    {
        var text = value.Trim();
        var same = ways.FirstOrDefault(w => string.Equals(w.Value, text, StringComparison.OrdinalIgnoreCase));
        return [same ?? new ContactWay(label, text), .. ways.Where(w => w != same)];
    }

    // Notes

    public sealed record NoteLine(string Id, string Title, string Notebook, string Folder, string? Client, bool Pinned, string Changed, string Preview);

    public sealed record NoteDetail(NoteLine Note, string Text);

    public IReadOnlyList<NoteLine> ListNotes(string? search = null, string? client = null, int limit = 50)
    {
        var clientId = client is { Length: > 0 } ? FindClient(client).Id : null;
        return [.. notes.List()
            .Where(n => n.Deleted is null && (clientId is null || n.ClientId == clientId))
            .Where(n => string.IsNullOrWhiteSpace(search) || Holds(search, n.Title, n.Text))
            .OrderByDescending(n => n.Pinned).ThenByDescending(n => n.Changed)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(Line)];
    }

    public NoteDetail GetNote(string note)
    {
        var found = FindNote(note);
        return new NoteDetail(Line(found), RichText.ToText(notes.Writing(found.Id)));
    }

    /// <summary>A new note, in a notebook (the first one if none is named), linked to a client if asked.</summary>
    public NoteDetail AddNote(string title, string text, string? notebook = null, string? folder = null, string? client = null, bool pinned = false)
    {
        Check(AssistantLimits.TooLong(title, "the title", Short), AssistantLimits.TooLong(text, "the writing", Long));
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Give the note a title or some writing.");
        var book = notebook is { Length: > 0 } ? FindNotebook(notebook) : notes.Notebooks()[0];
        var place = folder is { Length: > 0 } ? book.Folders.FirstOrDefault(f => string.Equals(f, folder.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"{book.Name} has no folder called \"{folder.Trim()}\".") : "";
        var made = notes.New(book.Id, place, client is { Length: > 0 } ? FindClient(client).Id : null);
        notes.Save(made.Id, title ?? "", RichText.FromText(text));
        if (pinned)
            notes.Change(made.Id, n => n with { Pinned = true });
        Changed("notes");
        return GetNote(made.Id);
    }

    /// <summary>Changes a note: a new title, writing in place of the old or added at the end, pinned, moved or linked.</summary>
    public NoteDetail UpdateNote(string note, string? title = null, string? text = null, string? append = null, bool? pinned = null,
        string? client = null, string? notebook = null)
    {
        var found = FindNote(note);
        var before = RichText.ToText(notes.Writing(found.Id));
        Check(AssistantLimits.TooLong(title, "the title", Short), AssistantLimits.TooLong(text, "the writing", Long),
            AssistantLimits.TooLong(append, "the writing", Long), AssistantLimits.Emptying(before, text, "a note"));
        if (title is not null || text is not null || append is not null)
        {
            // Writing replaced is kept, formatting and all, so Settings can put it back.
            if (text is not null && before.Trim().Length > 0)
                KeepOld("notes", found.Id, $"Replaced the writing in {NoteRules.Name(found)}", "note", before, store.NoteBody(found.Id));
            // Added writing goes after what's there as it is, so formatting, tables and pictures in the note stay.
            var writing = text is not null ? RichText.FromText(text) : notes.Writing(found.Id);
            if (append is { Length: > 0 })
                writing.Blocks.AddRange(RichText.FromText(append.Trim()).Blocks);
            notes.Save(found.Id, string.IsNullOrWhiteSpace(title) ? found.Title : title, writing);
        }
        if (pinned is not null || client is { Length: > 0 } || notebook is { Length: > 0 })
        {
            var clientId = client is not { Length: > 0 } ? found.ClientId
                : client.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : FindClient(client).Id;
            var bookId = notebook is { Length: > 0 } ? FindNotebook(notebook).Id : found.NotebookId;
            notes.Change(found.Id, n => n with
            {
                Pinned = pinned ?? n.Pinned,
                ClientId = clientId,
                NotebookId = bookId,
                Folder = bookId == n.NotebookId ? n.Folder : "",
            });
        }
        Changed("notes");
        return GetNote(found.Id);
    }

    // Calendar

    public sealed record EventLine(string Id, string Title, string Date, string? Start, string? End, string? Location, string? Client, string? Notes);

    public IReadOnlyList<EventLine> ListEvents(string? from = null, string? to = null)
    {
        var (fromGiven, fromDay, fromProblem) = AssistantLookup.Date(from, "from");
        var (toGiven, toDay, toProblem) = AssistantLookup.Date(to, "to");
        if (fromProblem.Length > 0 || toProblem.Length > 0)
            throw new ArgumentException(fromProblem.Length > 0 ? fromProblem : toProblem);
        var start = fromGiven && fromDay is { } f ? f : Today;
        var end = toGiven && toDay is { } t ? t : start.AddDays(14);
        if (end < start)
            throw new ArgumentException("to comes before from.");
        if (end.DayNumber - start.DayNumber > 366)
            throw new ArgumentException("Ask for a year at most.");
        return [.. store.ListEvents(start, end).OrderBy(e => e.Date).ThenBy(e => e.Start ?? TimeOnly.MinValue).Select(Line)];
    }

    public EventLine AddEvent(string title, string date, string? start = null, string? end = null, string? location = null, string? notes = null,
        string? client = null)
    {
        Check(AssistantLimits.TooLong(title, "the title", Short), AssistantLimits.TooLong(location, "the place", 2000), AssistantLimits.TooLong(notes, "the notes", Long));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Give the event a title.");
        var day = RequiredDate(date, "date");
        var (from, until) = Times(start, end, null, null);
        var saved = store.SaveEvent(new CalendarEvent
        {
            Id = CalendarEvent.NewId(),
            Title = title.Trim(),
            Date = day,
            Start = from,
            End = until ?? from?.AddHours(1),
            Location = Blank(location),
            Notes = Blank(notes),
            Customer = client is { Length: > 0 } ? FindClient(client).Name : null,
        });
        Changed("calendar");
        return Line(saved);
    }

    /// <summary>Moves or changes an event. Only what's given changes; start "none" makes it all day.</summary>
    public EventLine UpdateEvent(string calendarEvent, string? title = null, string? date = null, string? start = null, string? end = null,
        string? location = null, string? notes = null, string? client = null)
    {
        var found = FindEvent(calendarEvent);
        Check(AssistantLimits.TooLong(title, "the title", Short), AssistantLimits.TooLong(location, "the place", 2000), AssistantLimits.TooLong(notes, "the notes", Long),
            AssistantLimits.Emptying(found.Notes, notes, "an event's notes"));
        if (notes is not null && !string.IsNullOrWhiteSpace(found.Notes) && found.Notes != notes.Trim())
            KeepOld("calendar", found.Id, $"Replaced the notes on {found.Title}", "event-notes", found.Notes);
        var day = date is { Length: > 0 } ? RequiredDate(date, "date") : found.Date;
        var (from, until) = Times(start, end, found.Start, found.End);
        var saved = store.SaveEvent(found with
        {
            Title = string.IsNullOrWhiteSpace(title) ? found.Title : title.Trim(),
            Date = day,
            Start = from,
            End = from is null ? null : until ?? from.Value.AddHours(1),
            Location = location is null ? found.Location : Blank(location),
            Notes = notes is null ? found.Notes : Blank(notes),
            Customer = client is not { Length: > 0 } ? found.Customer
                : client.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : FindClient(client).Name,
        });
        Changed("calendar");
        return Line(saved);
    }

    private static (TimeOnly? Start, TimeOnly? End) Times(string? start, string? end, TimeOnly? oldStart, TimeOnly? oldEnd)
    {
        TimeOnly? Read(string? typed, TimeOnly? old, string what)
        {
            if (typed is null || typed.Trim().Length == 0)
                return old;
            if (typed.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
                return null;
            return CalendarEvents.TryParseTime(typed, out var time) ? time : throw new ArgumentException($"Give {what} like 09:30 or 14:00.");
        }
        var from = Read(start, oldStart, "start");
        var until = Read(end, start is { Length: > 0 } && end is null && oldStart is { } s && oldEnd is { } e && from is { } f ? f.Add(e - s) : oldEnd, "end");
        if (from is { } a && until is { } b && b <= a)
            throw new ArgumentException("end comes before start.");
        return (from, until);
    }

    // Time

    public sealed record TimeLine(string Id, string Date, int Minutes, string Duration, string Note, string? Client, string? Project);

    public IReadOnlyList<TimeLine> ListTime(string? from = null, string? to = null, string? client = null)
    {
        var (fromGiven, fromDay, fromProblem) = AssistantLookup.Date(from, "from");
        var (toGiven, toDay, toProblem) = AssistantLookup.Date(to, "to");
        if (fromProblem.Length > 0 || toProblem.Length > 0)
            throw new ArgumentException(fromProblem.Length > 0 ? fromProblem : toProblem);
        var end = toGiven && toDay is { } t ? t : Today;
        var start = fromGiven && fromDay is { } f ? f : end.AddDays(-30);
        var clientId = client is { Length: > 0 } ? FindClient(client).Id : null;
        var all = clients.List();
        return [.. store.ListTime(start, end).Where(e => clientId is null || e.ClientId == clientId)
            .OrderByDescending(e => e.Date).Select(e => Line(e, all))];
    }

    /// <summary>Time spent, for a client (and one of their projects) or not, on a day.</summary>
    public TimeLine LogTime(string duration, string note, string? date = null, string? client = null, string? project = null)
    {
        Check(AssistantLimits.TooLong(note, "the note", 2000));
        var minutes = TimeRules.ParseMinutes(duration ?? "")
            ?? throw new ArgumentException($"Give the time like 1:30, 45m or 2h (at most {TimeRules.MaxMinutes / 60} hours).");
        var (given, day, problem) = AssistantLookup.Date(date, "date");
        if (problem.Length > 0)
            throw new ArgumentException(problem);
        Client? owner = client is { Length: > 0 } ? FindClient(client) : null;
        string? projectId = null;
        if (project is { Length: > 0 })
        {
            if (owner is null)
                throw new ArgumentException("Name the client the project belongs to.");
            var (picked, projectProblem) = AssistantLookup.Find(owner.Projects.Where(p => !p.Finished), project, p => p.Id, p => p.Name, "project");
            projectId = (picked ?? throw new ArgumentException(projectProblem + $" {owner.Name}'s projects are on their client page.")).Id;
        }
        var saved = store.SaveTime(new TimeEntry
        {
            Id = TimeEntry.NewId(),
            Date = given && day is { } d ? d : Today,
            Minutes = minutes,
            Note = (note ?? "").Trim(),
            ClientId = owner?.Id,
            ProjectId = projectId,
            Created = clock.GetUtcNow(),
        });
        Changed("time");
        return Line(saved, clients.List());
    }

    // Renewals and retainers

    public sealed record RenewalLine(string Id, string Name, string Kind, string? Client, string? Provider, string RenewsOn, string When,
        int PeriodMonths, decimal? Cost, decimal? Price, int RemindDaysBefore, string? Notes);

    public sealed record RetainerLine(string Id, string Client, string Title, decimal MonthlyAmount, int DayOfMonth, string Starts, string? Ends, bool Paused);

    public IReadOnlyList<RenewalLine> ListRenewals(string? client = null)
    {
        var key = client is { Length: > 0 } ? ClientDirectory.Key(FindClient(client).Name) : null;
        return [.. store.ListRenewals().Where(r => key is null || ClientDirectory.Key(r.ClientName ?? "") == key).OrderBy(r => r.RenewsOn).Select(Line)];
    }

    public IReadOnlyList<RetainerLine> ListRetainers() => [.. store.ListRetainers().OrderBy(r => r.ClientName, StringComparer.CurrentCultureIgnoreCase).Select(Line)];

    public RenewalLine AddRenewal(string name, string renewsOn, string? kind = null, string? client = null, string? provider = null, int? periodMonths = null,
        decimal? cost = null, decimal? price = null, int? remindDaysBefore = null, string? notes = null)
    {
        Check(AssistantLimits.TooLong(name, "the name", Short), AssistantLimits.TooLong(provider, "the provider", Short), AssistantLimits.TooLong(notes, "the notes", Long));
        var renewal = new Renewal
        {
            Id = Renewal.NewId(),
            Name = (name ?? "").Trim(),
            Kind = Kind(kind) ?? RenewalKind.Domain,
            ClientName = client is { Length: > 0 } ? FindClient(client).Name : null,
            Provider = Blank(provider),
            RenewsOn = RequiredDate(renewsOn, "renews_on"),
            PeriodMonths = periodMonths ?? 12,
            Cost = cost,
            Price = price,
            RemindDaysBefore = remindDaysBefore ?? 30,
            Notes = Blank(notes),
            Created = clock.GetUtcNow(),
        };
        return SaveRenewal(renewal);
    }

    /// <summary>Changes a renewal. Only what's given changes.</summary>
    public RenewalLine UpdateRenewal(string renewal, string? name = null, string? renewsOn = null, string? kind = null, string? client = null,
        string? provider = null, int? periodMonths = null, decimal? cost = null, decimal? price = null, int? remindDaysBefore = null, string? notes = null)
    {
        var found = FindRenewal(renewal);
        Check(AssistantLimits.TooLong(name, "the name", Short), AssistantLimits.TooLong(provider, "the provider", Short), AssistantLimits.TooLong(notes, "the notes", Long),
            AssistantLimits.Emptying(found.Notes, notes, "a renewal's notes"));
        if (notes is not null && !string.IsNullOrWhiteSpace(found.Notes) && found.Notes != notes.Trim())
            KeepOld("renewals", found.Id, $"Replaced the notes on {found.Name}", "renewal-notes", found.Notes);
        return SaveRenewal(found with
        {
            Name = string.IsNullOrWhiteSpace(name) ? found.Name : name.Trim(),
            RenewsOn = renewsOn is { Length: > 0 } ? RequiredDate(renewsOn, "renews_on") : found.RenewsOn,
            Kind = Kind(kind) ?? found.Kind,
            ClientName = client is not { Length: > 0 } ? found.ClientName
                : client.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : FindClient(client).Name,
            Provider = provider is null ? found.Provider : Blank(provider),
            PeriodMonths = periodMonths ?? found.PeriodMonths,
            Cost = cost ?? found.Cost,
            Price = price ?? found.Price,
            RemindDaysBefore = remindDaysBefore ?? found.RemindDaysBefore,
            Notes = notes is null ? found.Notes : Blank(notes),
        });
    }

    private RenewalLine SaveRenewal(Renewal renewal)
    {
        var problems = RenewalRules.Check(renewal);
        if (problems.Count > 0)
            throw new ArgumentException(string.Join(" ", problems));
        var saved = store.SaveRenewal(renewal);
        Changed("renewals");
        return Line(saved);
    }

    private static RenewalKind? Kind(string? typed) => (typed ?? "").Trim().ToLowerInvariant() switch
    {
        "" => null,
        "domain" => RenewalKind.Domain,
        "hosting" => RenewalKind.Hosting,
        "ssl" or "certificate" => RenewalKind.Ssl,
        "other" => RenewalKind.Other,
        _ => throw new ArgumentException("kind is domain, hosting, ssl or other."),
    };

    // Finding things

    private Client FindClient(string typed)
    {
        if (clients.Find(typed) is { } exact)
            return exact;
        var (found, problem) = AssistantLookup.Find(clients.List(), typed, c => c.Id, c => c.Name, "client");
        return found ?? throw new ArgumentException(problem);
    }

    private Person FindPerson(string typed)
    {
        var people = contacts.List();
        if (people.FirstOrDefault(p => p.Emails.Any(e => string.Equals(e.Value, typed.Trim(), StringComparison.OrdinalIgnoreCase))) is { } byEmail)
            return byEmail;
        var (found, problem) = AssistantLookup.Find(people, typed, p => p.Id, p => p.Name, "person");
        return found ?? throw new ArgumentException(problem);
    }

    private Note FindNote(string typed)
    {
        var (found, problem) = AssistantLookup.Find(notes.List().Where(n => n.Deleted is null), typed, n => n.Id, n => NoteRules.Name(n), "note");
        return found ?? throw new ArgumentException(problem);
    }

    private Notebook FindNotebook(string typed)
    {
        var (found, problem) = AssistantLookup.Find(notes.Notebooks(), typed, n => n.Id, n => n.Name, "notebook");
        return found ?? throw new ArgumentException(problem);
    }

    private CalendarEvent FindEvent(string typed)
    {
        if (store.GetEvent(typed.Trim()) is { } byId)
            return byId;
        var (found, problem) = AssistantLookup.Find(store.ListEvents(Today.AddDays(-31), Today.AddDays(366)), typed, e => e.Id, e => e.Title, "event");
        return found ?? throw new ArgumentException(problem + " Use the id from list_events.");
    }

    private Renewal FindRenewal(string typed)
    {
        var (found, problem) = AssistantLookup.Find(store.ListRenewals(), typed, r => r.Id, r => r.Name, "renewal");
        return found ?? throw new ArgumentException(problem);
    }

    private static DateOnly RequiredDate(string? typed, string what)
    {
        var (given, day, problem) = AssistantLookup.Date(typed, what);
        if (problem.Length > 0)
            throw new ArgumentException(problem);
        return given && day is { } d ? d : throw new ArgumentException($"Give {what} as a date like 2026-11-14.");
    }

    private static string? Blank(string? typed) => string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();

    private static bool Holds(string search, params string?[] fields) =>
        fields.Any(f => f is not null && f.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase));

    // Shaping what goes back

    private static ClientLine Line(Client c) => new(c.Id, c.Name, c.ContactPerson, c.Email, c.Phone, c.Website, c.HourlyRate);

    private PersonLine Line(Person p) => new(p.Id, p.Name, p.Company, p.Role, [.. p.Emails.Select(e => e.Value)], [.. p.Phones.Select(e => e.Value)],
        p.ClientId is { } id ? clients.Get(id)?.Name : null, p.Groups, p.Notes);

    private NoteLine Line(Note n)
    {
        var book = notes.Notebooks().FirstOrDefault(b => b.Id == n.NotebookId)?.Name ?? "";
        return new NoteLine(n.Id, NoteRules.Name(n), book, n.Folder, n.ClientId is { } id ? clients.Get(id)?.Name : null, n.Pinned,
            n.Changed.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), n.Text.Length > 160 ? n.Text[..160] + "…" : n.Text);
    }

    private static EventLine Line(CalendarEvent e) => new(e.Id, e.Title, AssistantLookup.Day(e.Date)!, Clock(e.Start), Clock(e.End), e.Location, e.Customer, e.Notes);

    private static string? Clock(TimeOnly? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static TimeLine Line(TimeEntry e, IReadOnlyList<Client> all)
    {
        var owner = e.ClientId is { } id ? all.FirstOrDefault(c => c.Id == id) : null;
        return new TimeLine(e.Id, AssistantLookup.Day(e.Date)!, e.Minutes, TimeRules.Format(e.Minutes), e.Note, owner?.Name,
            owner?.Projects.FirstOrDefault(p => p.Id == e.ProjectId)?.Name);
    }

    private RenewalLine Line(Renewal r) => new(r.Id, r.Name, RenewalRules.KindLabel(r.Kind), r.ClientName, r.Provider, AssistantLookup.Day(r.RenewsOn)!,
        RenewalRules.When(r, Today), r.PeriodMonths, r.Cost, r.Price, r.RemindDaysBefore, r.Notes);

    private static RetainerLine Line(Retainer r) => new(r.Id, r.ClientName, r.Title, r.MonthlyAmount, r.DayOfMonth, AssistantLookup.Day(r.Starts)!,
        AssistantLookup.Day(r.Ends), r.Paused);
}
