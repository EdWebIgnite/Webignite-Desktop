using Webignite.Core.Writing;

namespace Webignite.Core.Mail;

/// <summary>
/// A whole mail kept to start others from: who it's to, the subject and the formatted writing (pictures and all),
/// without a signature, which comes from the mailbox it's sent from. {name} and {client} are filled in as in saved
/// replies.
/// </summary>
public sealed record MailTemplate(string Id, string Name, string Subject, string To, string Cc, string Html)
{
    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

public static class MailTemplates
{
    /// <summary>What's wrong before a template can be kept; empty when it's fine.</summary>
    public static IReadOnlyList<string> Check(string name, RichDocument writing, string subject, IEnumerable<MailTemplate> existing)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
            problems.Add("Give the template a name.");
        else if (existing.Any(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            problems.Add($"There's a template called {name.Trim()} already.");
        if (writing.IsBlank && string.IsNullOrWhiteSpace(subject))
            problems.Add("Write the mail first, then keep it as a template.");
        return problems;
    }

    /// <summary>A mail being written as a template: the signature taken off, and empty lines at the end too.</summary>
    public static MailTemplate From(string name, string to, string cc, string subject, RichDocument writing, string id)
    {
        var body = writing.Clone();
        RichEditing.RemoveSignature(body);
        while (body.Blocks.Count > 1 && body.Blocks[^1] is TextBlock { Text: var text } && text.Trim().Length == 0)
            body.Blocks.RemoveAt(body.Blocks.Count - 1);
        return new MailTemplate(id, name.Trim(), subject.Trim(), to.Trim(), cc.Trim(), RichHtml.Write(body, RichHtml.ToDataAddress));
    }

    /// <summary>
    /// The template's writing with {name} and {client} filled in, the mailbox's signature under it after a blank line.
    /// </summary>
    public static RichDocument Writing(MailTemplate template, string? firstName, string? clientName, IReadOnlyList<Block> signature)
    {
        var document = RichHtml.Read(SavedReplies.Fill(template.Html, firstName, clientName), RichHtml.DataAddress);
        if (signature.Count > 0)
        {
            document.Blocks.Add(new TextBlock());
            RichEditing.InsertSignature(document, signature);
        }
        return document;
    }

    public static string Subject(MailTemplate template, string? firstName, string? clientName) =>
        SavedReplies.Fill(template.Subject, firstName, clientName).Trim();
}
