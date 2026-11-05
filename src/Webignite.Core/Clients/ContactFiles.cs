using System.Globalization;
using System.Text;

namespace Webignite.Core.Clients;

/// <summary>
/// People to and from files: vCard (.vcf), which phones, Outlook and Google read, and CSV for spreadsheets.
/// Reading takes the headers Outlook and Google Contacts write. Nothing here syncs; a file is read or written once.
/// </summary>
public static class ContactFiles
{
    // vCard

    /// <summary>Everyone as one vCard 3.0 file.</summary>
    public static string ToVCard(IEnumerable<Person> people, Func<string?, string?> clientName)
    {
        var text = new StringBuilder();
        foreach (var person in people)
        {
            text.Append("BEGIN:VCARD\r\nVERSION:3.0\r\n");
            Line(text, "FN", Escape(person.Name));
            var (first, last) = SplitName(person.Name);
            Line(text, "N", $"{Escape(last)};{Escape(first)};;;");
            if ((clientName(person.ClientId) ?? person.Company) is { Length: > 0 } company)
                Line(text, "ORG", Escape(company));
            if (person.Role is { Length: > 0 } role)
                Line(text, "TITLE", Escape(role));
            foreach (var email in person.Emails)
                Line(text, $"EMAIL;TYPE=INTERNET,{EmailType(email.Label)}", Escape(email.Value));
            foreach (var phone in person.Phones)
                Line(text, $"TEL;TYPE={PhoneType(phone.Label)}", Escape(phone.Value));
            if (person.Groups.Count > 0)
                Line(text, "CATEGORIES", string.Join(',', person.Groups.Select(Escape)));
            if (person.Notes is { Length: > 0 } notes)
                Line(text, "NOTE", Escape(notes));
            text.Append("END:VCARD\r\n");
        }
        return text.ToString();
    }

    /// <summary>The people in a vCard file (versions 2.1, 3.0 and 4.0), as new people with no client yet.</summary>
    public static IReadOnlyList<Person> FromVCard(string text, DateTimeOffset now)
    {
        var people = new List<Person>();
        Person? current = null;
        foreach (var line in Unfold(text))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var head = line[..colon];
            var value = line[(colon + 1)..];
            var parts = head.Split(';');
            var name = parts[0].ToUpperInvariant();
            var dot = name.IndexOf('.');
            if (dot >= 0)
                name = name[(dot + 1)..]; // "item1.EMAIL" from Apple
            var options = parts.Skip(1).Select(p => p.ToUpperInvariant()).ToList();
            if (options.Any(o => o.Contains("QUOTED-PRINTABLE", StringComparison.Ordinal)))
                value = QuotedPrintable(value);
            switch (name)
            {
                case "BEGIN" when value.Equals("VCARD", StringComparison.OrdinalIgnoreCase):
                    current = new Person { Id = Person.NewId(), Name = "", Created = now, Changed = now };
                    break;
                case "END" when current is not null:
                    if (current.Name.Length == 0)
                        current = current with { Name = current.Email ?? "" };
                    if (current.Name.Length > 0)
                        people.Add(current);
                    current = null;
                    break;
                case "FN" when current is not null:
                    current = current with { Name = Unescape(value).Trim() };
                    break;
                case "N" when current is not null && current.Name.Length == 0:
                    var n = value.Split(';').Select(Unescape).ToArray();
                    current = current with { Name = string.Join(' ', new[] { n.ElementAtOrDefault(1), n.ElementAtOrDefault(0) }.Where(p => !string.IsNullOrWhiteSpace(p))).Trim() };
                    break;
                case "ORG" when current is not null:
                    current = current with { Company = Unescape(value.Split(';')[0]).Trim() };
                    break;
                case "TITLE" when current is not null:
                    current = current with { Role = Unescape(value).Trim() };
                    break;
                case "EMAIL" when current is not null && value.Trim().Length > 0:
                    current = current with { Emails = [.. current.Emails, new ContactWay(EmailLabel(options), Unescape(value).Trim())] };
                    break;
                case "TEL" when current is not null && value.Trim().Length > 0:
                    current = current with { Phones = [.. current.Phones, new ContactWay(PhoneLabel(options), Unescape(value).Replace("tel:", "", StringComparison.OrdinalIgnoreCase).Trim())] };
                    break;
                case "CATEGORIES" when current is not null:
                    current = current with { Groups = [.. SplitList(value)] };
                    break;
                case "NOTE" when current is not null:
                    current = current with { Notes = Unescape(value).Trim() };
                    break;
            }
        }
        return people;
    }

    private static void Line(StringBuilder text, string name, string value)
    {
        // Lines longer than 75 characters are folded: the rest goes on following lines, each starting with a space.
        var line = name + ":" + value;
        while (line.Length > 75)
        {
            text.Append(line[..75]).Append("\r\n");
            line = " " + line[75..];
        }
        text.Append(line).Append("\r\n");
    }

    private static IEnumerable<string> Unfold(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var current = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                current.Append(line, 1, line.Length - 1);
                continue;
            }
            // Quoted-printable lines ending in "=" carry on, in vCard 2.1.
            if (current.Length > 0 && current[^1] == '=' && current.ToString().Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
            {
                current.Length--;
                current.Append(line);
                continue;
            }
            if (current.Length > 0)
                yield return current.ToString();
            current.Clear().Append(line);
        }
        if (current.Length > 0)
            yield return current.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Unescape(string value)
    {
        var text = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                text.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
                text.Append(value[i]);
        }
        return text.ToString();
    }

    // "Hosting clients,Newsletter" with "\," inside a name kept.
    private static IEnumerable<string> SplitList(string value)
    {
        var current = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
                current.Append(value[++i]);
            else if (value[i] == ',')
            {
                if (current.ToString().Trim() is { Length: > 0 } item)
                    yield return item;
                current.Clear();
            }
            else
                current.Append(value[i]);
        }
        if (current.ToString().Trim() is { Length: > 0 } last)
            yield return last;
    }

    private static string QuotedPrintable(string value)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length && byte.TryParse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                bytes.Add(b);
                i += 2;
            }
            else
                bytes.AddRange(Encoding.UTF8.GetBytes(value[i].ToString()));
        }
        return Encoding.UTF8.GetString([.. bytes]);
    }

    private static (string First, string Last) SplitName(string name)
    {
        var cut = name.Trim().LastIndexOf(' ');
        return cut < 0 ? (name.Trim(), "") : (name[..cut].Trim(), name[(cut + 1)..].Trim());
    }

    private static string EmailType(string label) => label switch { "Personal" => "HOME", "Work" or "Accounts" => "WORK", _ => "OTHER" };
    private static string PhoneType(string label) => label switch { "Mobile" => "CELL", "Home" => "HOME", "Work" => "WORK", _ => "VOICE" };
    private static string EmailLabel(IReadOnlyList<string> options) => Has(options, "HOME") ? "Personal" : Has(options, "WORK") ? "Work" : "Work";
    private static string PhoneLabel(IReadOnlyList<string> options) => Has(options, "CELL") ? "Mobile" : Has(options, "HOME") ? "Home" : Has(options, "WORK") ? "Work" : "Mobile";
    private static bool Has(IReadOnlyList<string> options, string type) => options.Any(o => o.Split('=', ',').Contains(type, StringComparer.Ordinal));

    // CSV

    /// <summary>Everyone as a CSV file for a spreadsheet: one row a person, extra addresses and numbers joined with "; ".</summary>
    public static string ToCsv(IEnumerable<Person> people, Func<string?, string?> clientName)
    {
        var text = new StringBuilder("Name,Role,Client,Company,Email,Other emails,Phone,Other phones,Groups,Notes\r\n");
        foreach (var p in people)
        {
            text.AppendJoin(',', new[]
            {
                p.Name, p.Role, clientName(p.ClientId), p.Company, p.Email, string.Join("; ", p.Emails.Skip(1).Select(e => e.Value)),
                p.Phone, string.Join("; ", p.Phones.Skip(1).Select(e => e.Value)), string.Join("; ", p.Groups), p.Notes,
            }.Select(Cell)).Append("\r\n");
        }
        return text.ToString();
    }

    private static string Cell(string? value)
    {
        var text = value ?? "";
        // A cell starting with = + - or @ would run as a formula in a spreadsheet, so it gets an apostrophe in front.
        if (text.Length > 0 && "=+-@".Contains(text[0], StringComparison.Ordinal))
            text = "'" + text;
        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }

    /// <summary>The people in a CSV file, by the headers Webignite, Outlook and Google Contacts write.</summary>
    public static IReadOnlyList<Person> FromCsv(string text, DateTimeOffset now)
    {
        var rows = ReadCsv(text);
        if (rows.Count < 2)
            return [];
        var head = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => names.Select(n => head.IndexOf(n)).FirstOrDefault(i => i >= 0, -1);
        string Get(IReadOnlyList<string> row, int col) => col >= 0 && col < row.Count ? row[col].Trim() : "";
        IEnumerable<string> All(IReadOnlyList<string> row, Func<string, bool> header) =>
            head.Select((h, i) => (h, i)).Where(x => header(x.h)).Select(x => Get(row, x.i)).Where(v => v.Length > 0);

        var name = Col("name", "full name", "display name");
        var first = Col("first name", "given name");
        var last = Col("last name", "family name", "surname");
        var company = Col("company", "organisation", "organization", "organization 1 - name", "client");
        var role = Col("role", "job title", "title", "organization 1 - title");
        var notes = Col("notes");
        var groups = Col("groups", "categories", "group membership");
        var people = new List<Person>();
        foreach (var row in rows.Skip(1))
        {
            var full = Get(row, name);
            if (full.Length == 0)
                full = string.Join(' ', new[] { Get(row, first), Get(row, last) }.Where(p => p.Length > 0));
            var emails = All(row, h => h.Contains("e-mail", StringComparison.Ordinal) || h.Contains("email", StringComparison.Ordinal))
                .Where(v => v.Contains('@')).SelectMany(v => v.Split([';', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(v => v.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase).Select((v, i) => new ContactWay(i == 0 ? "Work" : "Other", v)).ToList();
            var phones = All(row, h => (h.Contains("phone", StringComparison.Ordinal) || h.Contains("mobile", StringComparison.Ordinal)) && !h.Contains("type", StringComparison.Ordinal))
                .SelectMany(v => v.Split([';', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct().Select((v, i) => new ContactWay(i == 0 ? "Mobile" : "Work", v)).ToList();
            if (full.Length == 0)
                full = emails.FirstOrDefault()?.Value ?? "";
            if (full.Length == 0)
                continue;
            people.Add(new Person
            {
                Id = Person.NewId(),
                Name = full,
                Company = Get(row, company) is { Length: > 0 } c ? c : null,
                Role = Get(row, role) is { Length: > 0 } r ? r : null,
                Notes = Get(row, notes) is { Length: > 0 } nt ? nt : null,
                Emails = emails,
                Phones = phones,
                Groups = ContactRules.Groups(Get(row, groups).Replace(" ::: ", ",", StringComparison.Ordinal).Replace(';', ',').Split(',')
                    .Where(g => !g.Trim().StartsWith('*'))), // Google's "* myContacts" isn't a group
                Created = now,
                Changed = now,
            });
        }
        return people;
    }

    private static List<IReadOnlyList<string>> ReadCsv(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var separator = Separator(text);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                    cell.Append(c);
            }
            else if (c == '"')
                quoted = true;
            else if (c == separator)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(cell.ToString());
                cell.Clear();
                if (row.Any(v => v.Length > 0))
                    rows.Add(row);
                row = [];
            }
            else
                cell.Append(c);
        }
        row.Add(cell.ToString());
        if (row.Any(v => v.Length > 0))
            rows.Add(row);
        return rows;
    }

    // Excel in South Africa often saves CSV with semicolons; the header line says which it is.
    private static char Separator(string text)
    {
        var header = text.Split('\n')[0];
        return header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';
    }

    // Bringing people in

    /// <summary>What importing would do: people to add, and how many are here already (by an email address, or by name).</summary>
    public sealed record ImportPlan(IReadOnlyList<Person> ToAdd, int AlreadyHere);

    /// <summary>
    /// The people from a file who aren't in Contacts yet, each with the client whose name is their company. Two
    /// in the file with one address come in once.
    /// </summary>
    public static ImportPlan Plan(IReadOnlyList<Person> existing, IEnumerable<Person> imported, IReadOnlyList<Client> clients)
    {
        var emails = new HashSet<string>(existing.SelectMany(p => p.Emails.Select(e => e.Value)), StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(existing.Where(p => p.Emails.Count == 0).Select(p => ClientDirectory.Key(p.Name)));
        var add = new List<Person>();
        var already = 0;
        foreach (var person in imported)
        {
            var known = person.Emails.Count > 0 ? person.Emails.Any(e => emails.Contains(e.Value)) : names.Contains(ClientDirectory.Key(person.Name));
            if (known)
            {
                already++;
                continue;
            }
            foreach (var email in person.Emails)
                emails.Add(email.Value);
            if (person.Emails.Count == 0)
                names.Add(ClientDirectory.Key(person.Name));
            var client = ClientDirectory.Find(clients, person.Company);
            add.Add(ContactRules.Tidy(person with { ClientId = client?.Id }));
        }
        return new ImportPlan(add, already);
    }

    /// <summary>"Added 12 people. 3 were in Contacts already."</summary>
    public static string Summary(ImportPlan plan) =>
        (plan.ToAdd.Count == 1 ? "Added 1 person." : $"Added {plan.ToAdd.Count} people.")
        + (plan.AlreadyHere switch { 0 => "", 1 => " 1 was in Contacts already.", var n => $" {n} were in Contacts already." });
}
