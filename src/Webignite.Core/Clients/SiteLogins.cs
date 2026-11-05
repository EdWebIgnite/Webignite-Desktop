namespace Webignite.Core.Clients;

/// <summary>
/// A login for a site Webignite looks after for a client, e.g. the WordPress admin or the hosting panel.
/// Kept in its own table, encrypted like everything else, and never handed to assistants or the connector.
/// </summary>
public sealed record SiteLogin
{
    public required string Id { get; init; }
    public required string ClientId { get; init; }

    /// <summary>What the login is for, e.g. "WordPress admin" or "Afrihost cPanel".</summary>
    public required string Site { get; init; }

    /// <summary>Where to sign in, e.g. https://karoocoffee.co.za/wp-admin.</summary>
    public string? Address { get; init; }

    public string? UserName { get; init; }
    public string? Password { get; init; }

    /// <summary>Anything else needed to get in, e.g. "2FA code goes to Pieter's phone".</summary>
    public string? Notes { get; init; }

    public DateTimeOffset Changed { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

/// <summary>Keeping a client's site logins in order.</summary>
public static class SiteLogins
{
    public const int MaxText = 500;
    public const int MaxNotes = 2000;

    /// <summary>How long a copied password stays on the clipboard before Webignite clears it.</summary>
    public static readonly TimeSpan ClipboardLife = TimeSpan.FromSeconds(30);

    /// <summary>What stands in for a password on screen until Show is clicked.</summary>
    public const string Hidden = "••••••••";

    /// <summary>What's wrong with a login before it can be saved; empty when it's fine.</summary>
    public static IReadOnlyList<string> Check(SiteLogin login)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(login.Site))
            problems.Add("Give it a site name, e.g. WordPress admin.");
        if (new[] { login.Site, login.Address, login.UserName, login.Password }.Any(t => t?.Length > MaxText))
            problems.Add($"Keep each field under {MaxText} characters.");
        if (login.Notes?.Length > MaxNotes)
            problems.Add($"Keep the notes under {MaxNotes} characters.");
        if (!string.IsNullOrWhiteSpace(login.Address) && Link(login.Address) is null)
            problems.Add("The login address doesn't look right. Type it like karoocoffee.co.za/wp-admin.");
        return problems;
    }

    /// <summary>Trims what was typed and blanks empty fields. The password keeps its spaces, as they may be part of it.</summary>
    public static SiteLogin Tidy(SiteLogin login) => login with
    {
        Site = login.Site.Trim(),
        Address = Blank(login.Address),
        UserName = Blank(login.UserName),
        Password = string.IsNullOrEmpty(login.Password) ? null : login.Password,
        Notes = Blank(login.Notes),
    };

    /// <summary>
    /// The address to open, with https:// added when it was typed without one. Null for anything that isn't a
    /// web address, so a typed "file:" or "javascript:" link can never be opened from here.
    /// </summary>
    public static Uri? Link(string? address)
    {
        var typed = address?.Trim();
        if (string.IsNullOrEmpty(typed) || typed.Any(char.IsWhiteSpace))
            return null;
        if (!typed.Contains("://", StringComparison.Ordinal))
            typed = "https://" + typed;
        return Uri.TryCreate(typed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.Host.Length > 0
            ? uri
            : null;
    }

    /// <summary>The address as it's shown on the row: no https://, no trailing slash.</summary>
    public static string Shown(string? address)
    {
        if (Link(address) is not { } uri)
            return address?.Trim() ?? "";
        var port = uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return (uri.Host + port + uri.GetComponents(UriComponents.PathAndQuery, UriFormat.Unescaped)).TrimEnd('/');
    }

    /// <summary>A–Z by site name, then by user name.</summary>
    public static IReadOnlyList<SiteLogin> Ordered(IEnumerable<SiteLogin> logins) =>
        [.. logins.OrderBy(l => l.Site, StringComparer.CurrentCultureIgnoreCase).ThenBy(l => l.UserName, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Whether every typed word is in the site name, address, user name or notes. Never the password.</summary>
    public static bool Matches(SiteLogin login, string? words)
    {
        if (string.IsNullOrWhiteSpace(words))
            return true;
        var haystack = string.Join(' ', login.Site, login.Address, login.UserName, login.Notes);
        return words.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => haystack.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
