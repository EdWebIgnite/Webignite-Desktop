namespace Webignite.Core.Documents;

/// <summary>
/// A field of Eddie's own on a quote or invoice, e.g. Project: "Spar Brits website". Kept by name, with whether it
/// prints for the client, as the field was set up when it was filled in.
/// </summary>
public sealed record DocumentField(string Name, string Value, bool Print = true);

/// <summary>A field quotes and invoices offer, set up once in Settings: its name, and whether it prints on the PDF.</summary>
public sealed record FieldDefinition(string Name, bool Print = true);

/// <summary>
/// Fields of Eddie's own on quotes and invoices, for white-label work and the like: set up once, filled in per
/// document, printed on the PDF when the field says so. Project is there to begin with.
/// </summary>
public static class DocumentFields
{
    public const string Project = "Project";

    public static readonly IReadOnlyList<FieldDefinition> Usual = [new(Project)];

    /// <summary>A field's value on a document, or empty.</summary>
    public static string Value(Document doc, string name) =>
        doc.Fields.FirstOrDefault(f => Same(f.Name, name))?.Value ?? "";

    /// <summary>The document with a field set, or taken off when the value is empty. Other fields keep their place.</summary>
    public static Document With(Document doc, string name, string? value, bool print = true)
    {
        var trimmed = value?.Trim() ?? "";
        var fields = doc.Fields.ToList();
        var at = fields.FindIndex(f => Same(f.Name, name));
        if (trimmed.Length == 0)
        {
            if (at >= 0)
                fields.RemoveAt(at);
        }
        else if (at >= 0)
            fields[at] = fields[at] with { Value = trimmed, Print = print };
        else
            fields.Add(new DocumentField(name.Trim(), trimmed, print));
        return doc with { Fields = fields };
    }

    /// <summary>
    /// The fields to fill in on a document, in the order Settings has them, then any the document has that Settings
    /// no longer offers (so nothing typed is lost).
    /// </summary>
    public static IReadOnlyList<(FieldDefinition Field, string Value)> ToFill(IReadOnlyList<FieldDefinition> offered, Document doc)
    {
        var list = offered.Select(f => (f, Value(doc, f.Name))).ToList();
        list.AddRange(doc.Fields.Where(f => !offered.Any(o => Same(o.Name, f.Name))).Select(f => (new FieldDefinition(f.Name, f.Print), f.Value)));
        return list;
    }

    /// <summary>What prints for the client: filled fields, leaving out those set up as for us only.</summary>
    public static IReadOnlyList<DocumentField> Printed(Document doc) =>
        [.. doc.Fields.Where(f => f.Print && f.Value.Trim().Length > 0)];

    /// <summary>
    /// The reference Xero gets, as Xero makes the PDF that is mailed: the reference, then each printed field by name,
    /// e.g. "Website November · Project: Spar Brits". Xero takes up to 255 characters.
    /// </summary>
    public static string XeroReference(Document doc)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(doc.Reference))
            parts.Add(doc.Reference.Trim());
        parts.AddRange(Printed(doc).Select(f => $"{f.Name}: {f.Value.Trim()}"));
        var joined = string.Join(" · ", parts);
        return joined.Length <= 255 ? joined : joined[..255];
    }

    /// <summary>Why a new field can't be added under this name, or null when it can.</summary>
    public static string? Problem(IReadOnlyList<FieldDefinition> offered, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return "Give the field a name.";
        if (trimmed.Length > 40)
            return "Keep the name under 40 letters.";
        if (offered.Any(f => Same(f.Name, trimmed)))
            return "There is a field with that name already.";
        if (trimmed is "Date" or "Due" or "Valid until" or "Reference" or "Number")
            return "That name is already on every quote and invoice.";
        return null;
    }

    /// <summary>
    /// Project names to pick from on a client's quote or invoice: the client's own projects still running, then their
    /// Planner projects, each once. Anything else can still be typed.
    /// </summary>
    public static IReadOnlyList<string> ProjectChoices(Webignite.Core.Clients.Client? client, IEnumerable<Webignite.Core.Projects.Project> planner)
    {
        if (client is null)
            return [];
        return [.. client.Projects.Where(p => !p.Finished).Select(p => p.Name)
            .Concat(planner.Where(p => p.ClientId == client.Id && p.Status != Webignite.Core.Projects.ProjectStatus.Done).Select(p => p.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
