using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Webignite.Core.Documents;
using Webignite.Services;

namespace Webignite.Connector;

/// <summary>
/// Clients, Contacts, Notes, the calendar, time and renewals for Claude and ChatGPT. They work as Eddie would in
/// the app and never delete anything. Site logins and passwords are never reachable from here.
/// </summary>
[McpServerToolType]
public sealed class BusinessTools(AssistantBusiness business)
{
    private const string ClientHint = "The client's name (or part of it, if only one matches) or its id.";
    private const string DateHint = "As yyyy-MM-dd.";
    private const string Changes = "Only what you give changes. Nothing can be deleted.";

    // Clients

    [McpServerTool(Name = "list_clients", Title = "List clients", ReadOnly = true, OpenWorld = false)]
    [Description("Eddie's clients: name, contact person, email, phone, website and hourly rate (in Rand).")]
    public string ListClients(
        [Description("Part of a name, contact, email or website.")] string? search = null,
        [Description("How many to return, 1 to 500. Default 100.")] int limit = 100) =>
        Json(business.ListClients(search, limit));

    [McpServerTool(Name = "get_client", Title = "Get a client", ReadOnly = true, OpenWorld = false)]
    [Description("One client in full: details, address, notes and their dated history, people in Contacts, linked notes, "
        + "renewals and retainers. Site logins are never shown.")]
    public string GetClient([Description(ClientHint)] string client) => Run(() => business.GetClient(client));

    [McpServerTool(Name = "add_client", Title = "Add a client", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Adds a client. If one with the same name is already there, that one comes back instead.")]
    public string AddClient(
        [Description("The business's name.")] string name,
        [Description("Who to deal with there.")] string? contact_person = null,
        [Description("Their email address.")] string? email = null,
        [Description("Their phone number.")] string? phone = null,
        [Description("Their website, e.g. karoocoffee.co.za.")] string? website = null,
        [Description("Their address, for invoices.")] string? address = null,
        [Description("Anything worth knowing about them.")] string? notes = null,
        [Description("Eddie's hourly rate for them, in Rand.")] decimal? hourly_rate = null,
        McpServer? server = null) =>
        Write(server, $"Added the client {name}", () => business.AddClient(name, contact_person, email, phone, website, address, notes, hourly_rate));

    [McpServerTool(Name = "update_client", Title = "Change a client", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Changes a client's details. " + Changes + " An empty value clears that detail.")]
    public string UpdateClient(
        [Description(ClientHint)] string client,
        [Description("A new name.")] string? name = null,
        [Description("Who to deal with there.")] string? contact_person = null,
        [Description("Their email address.")] string? email = null,
        [Description("Their phone number.")] string? phone = null,
        [Description("Their website.")] string? website = null,
        [Description("Their address.")] string? address = null,
        [Description("Notes about them, in place of the old ones.")] string? notes = null,
        [Description("Eddie's hourly rate for them, in Rand.")] decimal? hourly_rate = null,
        McpServer? server = null) =>
        Write(server, $"Changed the client {client}", () => business.UpdateClient(client, name, contact_person, email, phone, website, address, notes, hourly_rate));

    [McpServerTool(Name = "add_client_note", Title = "Add to a client's history", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Adds a dated note to a client's history, e.g. what was agreed on a call. Signed as from you.")]
    public string AddClientNote(
        [Description(ClientHint)] string client,
        [Description("The note.")] string text,
        [Description("The day it's about. " + DateHint + " Default today.")] string? date = null,
        McpServer? server = null) =>
        Write(server, $"Added to {client}'s history", () => business.AddClientNote(client, text, Assistant.Name(server), date));

    // Contacts

    [McpServerTool(Name = "list_contacts", Title = "List people in Contacts", ReadOnly = true, OpenWorld = false)]
    [Description("People in Contacts: name, company, role, emails, numbers, their client and groups.")]
    public string ListContacts(
        [Description("Part of a name, company, email, number or group.")] string? search = null,
        [Description("Only people at this client.")] string? client = null,
        [Description("How many to return, 1 to 500. Default 100.")] int limit = 100) =>
        Run(() => business.ListContacts(search, client, limit));

    [McpServerTool(Name = "add_contact", Title = "Add a person to Contacts", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Adds a person to Contacts. Someone with the same email already there comes back instead.")]
    public string AddContact(
        [Description("Their name.")] string name,
        [Description("Their email address.")] string? email = null,
        [Description("Their phone number.")] string? phone = null,
        [Description("Where they work.")] string? company = null,
        [Description("What they do there.")] string? role = null,
        [Description("The client they belong to.")] string? client = null,
        [Description("Anything worth knowing.")] string? notes = null,
        [Description("Groups, e.g. Suppliers.")] string[]? groups = null,
        McpServer? server = null) =>
        Write(server, $"Added {name} to Contacts", () => business.AddContact(name, email, phone, company, role, client, notes, groups));

    [McpServerTool(Name = "update_contact", Title = "Change a person in Contacts", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Changes a person. " + Changes + " A new email or number becomes their main one, keeping the others.")]
    public string UpdateContact(
        [Description("Their name, email or id.")] string contact,
        [Description("A new name.")] string? name = null,
        [Description("Their main email address.")] string? email = null,
        [Description("Their main phone number.")] string? phone = null,
        [Description("Where they work.")] string? company = null,
        [Description("What they do there.")] string? role = null,
        [Description("The client they belong to, or none.")] string? client = null,
        [Description("Notes, in place of the old ones.")] string? notes = null,
        [Description("The groups they're in, in place of the old ones.")] string[]? groups = null,
        McpServer? server = null) =>
        Write(server, $"Changed {contact} in Contacts", () => business.UpdateContact(contact, name, email, phone, company, role, client, notes, groups));

    // Notes

    [McpServerTool(Name = "list_notes", Title = "List notes", ReadOnly = true, OpenWorld = false)]
    [Description("Eddie's notes, pinned first then latest changed: title, notebook, folder, client and the start of the writing.")]
    public string ListNotes(
        [Description("Words in the title or writing.")] string? search = null,
        [Description("Only notes linked to this client.")] string? client = null,
        [Description("How many to return, 1 to 200. Default 50.")] int limit = 50) =>
        Run(() => business.ListNotes(search, client, limit));

    [McpServerTool(Name = "get_note", Title = "Read a note", ReadOnly = true, OpenWorld = false)]
    [Description("A note's full writing, as plain text with \"- \" lists and \"[ ] \" / \"[x] \" checklists.")]
    public string GetNote([Description("The note's title (or part of it) or id.")] string note) => Run(() => business.GetNote(note));

    [McpServerTool(Name = "add_note", Title = "Write a note", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Writes a new note. Plain text; \"- \" starts a list item and a blank line a new paragraph.")]
    public string AddNote(
        [Description("The note's title.")] string title,
        [Description("The writing.")] string text,
        [Description("The notebook to put it in. Default the first one.")] string? notebook = null,
        [Description("A folder in that notebook.")] string? folder = null,
        [Description("A client to link it to.")] string? client = null,
        [Description("true to pin it to the top.")] bool pinned = false,
        McpServer? server = null) =>
        Write(server, $"Wrote the note {title}", () => business.AddNote(title, text, notebook, folder, client, pinned));

    [McpServerTool(Name = "update_note", Title = "Change a note", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Changes a note: a new title, new writing in place of the old, or writing added at the end; pin it, move it to "
        + "another notebook, or link a client. " + Changes)]
    public string UpdateNote(
        [Description("The note's title (or part of it) or id.")] string note,
        [Description("A new title.")] string? title = null,
        [Description("New writing, in place of all the old.")] string? text = null,
        [Description("Writing to add at the end.")] string? append = null,
        [Description("true to pin, false to unpin.")] bool? pinned = null,
        [Description("A client to link, or none.")] string? client = null,
        [Description("Move it to this notebook.")] string? notebook = null,
        McpServer? server = null) =>
        Write(server, $"Changed the note {note}", () => business.UpdateNote(note, title, text, append, pinned, client, notebook));

    // Calendar

    [McpServerTool(Name = "list_events", Title = "List calendar events", ReadOnly = true, OpenWorld = false)]
    [Description("Events on Eddie's calendar between two days (both included). Default the next two weeks. Project tasks and "
        + "to-dos with due dates are under list_tasks and list_todos.")]
    public string ListEvents(
        [Description("The first day. " + DateHint + " Default today.")] string? from = null,
        [Description("The last day. " + DateHint + " Default two weeks on.")] string? to = null) =>
        Run(() => business.ListEvents(from, to));

    [McpServerTool(Name = "add_event", Title = "Add a calendar event", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Puts an event on Eddie's calendar. Leave start out for an all-day event. Check list_events first so you don't add it twice.")]
    public string AddEvent(
        [Description("What it is, e.g. Call with Karoo Coffee.")] string title,
        [Description("The day. " + DateHint)] string date,
        [Description("When it starts, as HH:mm.")] string? start = null,
        [Description("When it ends, as HH:mm. Default an hour after it starts.")] string? end = null,
        [Description("Where, or the meeting link.")] string? location = null,
        [Description("Any detail.")] string? notes = null,
        [Description("The client it's with.")] string? client = null,
        McpServer? server = null) =>
        Write(server, $"Put {title} on the calendar for {date}", () => business.AddEvent(title, date, start, end, location, notes, client));

    [McpServerTool(Name = "update_event", Title = "Change a calendar event", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Moves or changes an event. " + Changes + " Start none makes it all day.")]
    public string UpdateEvent(
        [Description("The event's id from list_events, or its title if only one fits.")] string calendar_event,
        [Description("A new title.")] string? title = null,
        [Description("A new day. " + DateHint)] string? date = null,
        [Description("A new start, as HH:mm, or none.")] string? start = null,
        [Description("A new end, as HH:mm.")] string? end = null,
        [Description("Where, or the meeting link.")] string? location = null,
        [Description("Notes, in place of the old ones.")] string? notes = null,
        [Description("The client it's with, or none.")] string? client = null,
        McpServer? server = null) =>
        Write(server, $"Changed the event {calendar_event}", () => business.UpdateEvent(calendar_event, title, date, start, end, location, notes, client));

    // Time

    [McpServerTool(Name = "list_time", Title = "List time logged", ReadOnly = true, OpenWorld = false)]
    [Description("Time Eddie logged between two days (both included), latest first. Default the last 30 days.")]
    public string ListTime(
        [Description("The first day. " + DateHint)] string? from = null,
        [Description("The last day. " + DateHint + " Default today.")] string? to = null,
        [Description("Only time for this client.")] string? client = null) =>
        Run(() => business.ListTime(from, to, client));

    [McpServerTool(Name = "log_time", Title = "Log time", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Logs time Eddie spent, for a client (and one of their projects) or not. It counts towards their next time invoice.")]
    public string LogTime(
        [Description("How long, like 1:30, 45m, 2h or 1.5 (hours).")] string duration,
        [Description("What it was for, e.g. Homepage layout.")] string note,
        [Description("The day. " + DateHint + " Default today.")] string? date = null,
        [Description("The client it was for.")] string? client = null,
        [Description("One of that client's projects, as on their client page.")] string? project = null,
        McpServer? server = null) =>
        Write(server, $"Logged {duration} for {note}", () => business.LogTime(duration, note, date, client, project));

    // Renewals and retainers

    [McpServerTool(Name = "list_renewals", Title = "List renewals", ReadOnly = true, OpenWorld = false)]
    [Description("Domains, hosting, certificates and anything else renewed on a date, soonest first: client, provider, when, "
        + "how often, what it costs Eddie and what the client pays (in Rand).")]
    public string ListRenewals([Description("Only this client's.")] string? client = null) => Run(() => business.ListRenewals(client));

    [McpServerTool(Name = "add_renewal", Title = "Add a renewal", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Adds something that renews on a date, so Eddie is reminded and the client is invoiced. Check list_renewals first.")]
    public string AddRenewal(
        [Description("What it is, e.g. karoocoffee.co.za.")] string name,
        [Description("When it next renews. " + DateHint)] string renews_on,
        [Description("domain, hosting, ssl or other. Default domain.")] string? kind = null,
        [Description("The client it's for.")] string? client = null,
        [Description("Who it's with, e.g. xneelo.")] string? provider = null,
        [Description("How many months each renewal covers. Default 12.")] int? period_months = null,
        [Description("What it costs Eddie, in Rand.")] decimal? cost = null,
        [Description("What the client pays, in Rand.")] decimal? price = null,
        [Description("How many days before to remind Eddie. Default 30.")] int? remind_days_before = null,
        [Description("Any detail.")] string? notes = null,
        McpServer? server = null) =>
        Write(server, $"Added the renewal {name}", () => business.AddRenewal(name, renews_on, kind, client, provider, period_months, cost, price, remind_days_before, notes));

    [McpServerTool(Name = "update_renewal", Title = "Change a renewal", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Changes a renewal. " + Changes)]
    public string UpdateRenewal(
        [Description("Its name or id.")] string renewal,
        [Description("A new name.")] string? name = null,
        [Description("When it next renews. " + DateHint)] string? renews_on = null,
        [Description("domain, hosting, ssl or other.")] string? kind = null,
        [Description("The client it's for, or none.")] string? client = null,
        [Description("Who it's with.")] string? provider = null,
        [Description("How many months each renewal covers.")] int? period_months = null,
        [Description("What it costs Eddie, in Rand.")] decimal? cost = null,
        [Description("What the client pays, in Rand.")] decimal? price = null,
        [Description("How many days before to remind Eddie.")] int? remind_days_before = null,
        [Description("Notes, in place of the old ones.")] string? notes = null,
        McpServer? server = null) =>
        Write(server, $"Changed the renewal {renewal}", () => business.UpdateRenewal(renewal, name, renews_on, kind, client, provider, period_months, cost, price, remind_days_before, notes));

    [McpServerTool(Name = "list_retainers", Title = "List retainers", ReadOnly = true, OpenWorld = false)]
    [Description("Monthly retainers: client, what it's for, the monthly amount in Rand, the day it's invoiced, and whether it's paused.")]
    public string ListRetainers() => Json(business.ListRetainers());

    // A change: made as the assistant asking, and written in the list of what Claude and ChatGPT changed.
    private string Write<T>(McpServer? server, string what, Func<T> work)
    {
        business.Who = Assistant.Name(server);
        var result = Run(work);
        business.Noted(what);
        return result;
    }

    private static string Run<T>(Func<T> work)
    {
        try
        {
            return Json(work());
        }
        catch (ArgumentException e)
        {
            throw new McpException(e.Message);
        }
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DocumentJson.Options);
}
