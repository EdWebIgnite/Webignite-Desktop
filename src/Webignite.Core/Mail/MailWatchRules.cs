namespace Webignite.Core.Mail;

/// <summary>
/// Mail that shows as it arrives: a quiet second connection per mailbox waits on the inbox (IMAP IDLE) and the
/// server says when mail lands. The check every few minutes stays, for servers that can't and for when the watch
/// is down.
/// </summary>
public static class MailWatchRules
{
    /// <summary>
    /// How long one wait lasts before it's started again. Servers may drop a wait after 30 minutes, and home
    /// routers forget quiet connections sooner, so it's kept well under both.
    /// </summary>
    public static readonly TimeSpan WaitFor = TimeSpan.FromMinutes(9);

    /// <summary>After the connection fails: half a minute, doubling each time, never more than 10 minutes.</summary>
    public static TimeSpan RetryAfter(int failures) =>
        TimeSpan.FromSeconds(Math.Min(600, 30 * Math.Pow(2, Math.Clamp(failures - 1, 0, 10))));
}
