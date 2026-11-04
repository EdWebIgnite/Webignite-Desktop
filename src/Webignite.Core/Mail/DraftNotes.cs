namespace Webignite.Core.Mail;

/// <summary>The note shown above a prepared mail, saying what still needs a look before it's sent.</summary>
public static class DraftNotes
{
    /// <summary>
    /// "Xero has no email address for Karoo. Type it in To." when nobody is in To, and "Couldn't fill [Due Date].
    /// Fix the text before sending." for placeholders left over, then any note of the caller's own. Null when
    /// there's nothing to say.
    /// </summary>
    public static string? For(int recipients, string noAddress, IReadOnlyCollection<string> unfilled, string? extra = null)
    {
        var notes = new List<string>();
        if (recipients == 0)
            notes.Add(noAddress);
        if (unfilled.Count > 0)
            notes.Add($"Couldn't fill {string.Join(", ", unfilled)}. Fix the text before sending.");
        if (!string.IsNullOrWhiteSpace(extra))
            notes.Add(extra.Trim());
        return notes.Count == 0 ? null : string.Join(" ", notes);
    }
}
