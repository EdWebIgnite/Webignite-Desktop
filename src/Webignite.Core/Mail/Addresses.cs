namespace Webignite.Core.Mail;

public static class Addresses
{
    public static bool LooksValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        email = email.Trim();
        var at = email.IndexOf('@');
        return at > 0 && at == email.LastIndexOf('@') && email.IndexOf('.', at) > at + 1
            && !email.EndsWith('.') && !email.Any(char.IsWhiteSpace);
    }
}
