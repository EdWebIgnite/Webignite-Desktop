namespace Webignite.Core.Mail;

/// <summary>
/// A mailbox the app reads and sends from. The password is never kept here; it lives in
/// Windows Credential Manager under the account's id.
/// </summary>
public sealed record MailAccount
{
    public required string Id { get; init; }

    /// <summary>The name people see on mail you send, e.g. "Eddie Teichert" or "Webignite Accounts".</summary>
    public required string DisplayName { get; init; }

    public required string Address { get; init; }
    public required string ImapHost { get; init; }
    public int ImapPort { get; init; } = 993;
    public required string SmtpHost { get; init; }
    public int SmtpPort { get; init; } = 465;

    /// <summary>Sign-in name. Most servers, xneelo included, use the full address.</summary>
    public string? UserName { get; init; }

    public string? Signature { get; init; }

    /// <summary>A logo or banner shown under the signature in the HTML version of mail sent from here.</summary>
    public byte[]? SignatureImage { get; init; }

    /// <summary>The picture's type, e.g. image/png.</summary>
    public string? SignatureImageType { get; init; }

    /// <summary>Whether invoices and quotes are sent from this mailbox.</summary>
    public bool SendsInvoices { get; init; }

    /// <summary>Whether Claude and ChatGPT may read this mailbox and draft replies in it.</summary>
    public bool AssistantsMayRead { get; init; } = true;

    public DateTimeOffset Created { get; init; }

    public string SignInName => string.IsNullOrWhiteSpace(UserName) ? Address : UserName;

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

public static class MailAccounts
{
    /// <summary>
    /// Server settings for a webignite.co.za mailbox on xneelo, so only the password has to be typed.
    /// Other domains get guesses in the usual mail.domain / smtp.domain shape.
    /// </summary>
    public static MailAccount Suggest(string address, string displayName)
    {
        var domain = address.Contains('@') ? address[(address.LastIndexOf('@') + 1)..].Trim().ToLowerInvariant() : "";
        return new MailAccount
        {
            Id = MailAccount.NewId(),
            DisplayName = displayName.Trim(),
            Address = address.Trim().ToLowerInvariant(),
            ImapHost = domain.Length == 0 ? "" : "mail." + domain,
            SmtpHost = domain.Length == 0 ? "" : "smtp." + domain,
            SendsInvoices = address.Trim().StartsWith("accounts@", StringComparison.OrdinalIgnoreCase),
        };
    }

    public static IReadOnlyList<string> Check(MailAccount account, IEnumerable<MailAccount> others)
    {
        var problems = new List<string>();
        if (!Addresses.LooksValid(account.Address))
            problems.Add("The email address doesn't look right.");
        else if (others.Any(o => o.Id != account.Id && string.Equals(o.Address, account.Address, StringComparison.OrdinalIgnoreCase)))
            problems.Add("That mailbox is already added.");
        if (string.IsNullOrWhiteSpace(account.DisplayName))
            problems.Add("Type the name people should see, like Eddie Teichert.");
        if (string.IsNullOrWhiteSpace(account.ImapHost) || account.ImapHost.Contains(' '))
            problems.Add("Type the incoming (IMAP) server.");
        if (string.IsNullOrWhiteSpace(account.SmtpHost) || account.SmtpHost.Contains(' '))
            problems.Add("Type the outgoing (SMTP) server.");
        if (account.ImapPort is < 1 or > 65535 || account.SmtpPort is < 1 or > 65535)
            problems.Add("Ports are numbers from 1 to 65535.");
        return problems;
    }
}

public static class SignatureImages
{
    /// <summary>Big enough for a logo or banner; small enough not to bloat every mail.</summary>
    public const int MaxBytes = 300 * 1024;

    /// <summary>The picture's type from its first bytes (PNG, JPEG or GIF), or a problem to show.</summary>
    public static (string? Type, string? Problem) Check(byte[] bytes)
    {
        if (bytes.Length > MaxBytes)
            return (null, $"That picture is {bytes.Length / 1024} KB. Use one under {MaxBytes / 1024} KB, for example a logo saved at about 400 pixels wide.");
        if (bytes is [0x89, (byte)'P', (byte)'N', (byte)'G', ..])
            return ("image/png", null);
        if (bytes is [0xFF, 0xD8, 0xFF, ..])
            return ("image/jpeg", null);
        if (bytes is [(byte)'G', (byte)'I', (byte)'F', ..])
            return ("image/gif", null);
        return (null, "Use a PNG, JPG or GIF picture.");
    }

    /// <summary>
    /// Whether the picture goes under this mail: when the mailbox has one and the signature text is
    /// still in the mail (deleting the signature drops the picture too).
    /// </summary>
    public static bool Belongs(MailAccount account, string body) =>
        account.SignatureImage is { Length: > 0 }
        && (string.IsNullOrWhiteSpace(account.Signature) || body.Contains(account.Signature.Trim(), StringComparison.Ordinal));
}
