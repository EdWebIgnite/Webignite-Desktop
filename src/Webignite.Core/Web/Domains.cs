namespace Webignite.Core.Web;

public static class Domains
{
    /// <summary>
    /// The one rule for turning a website, address or email into its domain, used by the site check, uptime
    /// and client mail matching: "https://www.Karoo.co.za/menu" becomes "karoo.co.za". Null when it isn't a domain.
    /// </summary>
    public static string? Of(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var text = input.Trim().ToLowerInvariant();
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
            text = text[(scheme + 3)..];
        text = text.Split('/', '?', '#')[0].Split(':')[0].TrimEnd('.');
        if (text.Contains('@'))
            text = text[(text.LastIndexOf('@') + 1)..];
        if (text.StartsWith("www.", StringComparison.Ordinal))
            text = text[4..];
        if (text.Length is 0 or > 253 || !text.Contains('.'))
            return null;
        foreach (var label in text.Split('.'))
        {
            if (label.Length is 0 or > 63 || label.StartsWith('-') || label.EndsWith('-') || !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                return null;
        }
        return text;
    }
}
