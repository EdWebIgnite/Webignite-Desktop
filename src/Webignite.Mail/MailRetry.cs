using System.Net.Sockets;
using MailKit;
using MailKit.Net.Imap;

namespace Webignite.Mail;

/// <summary>When a mail server job is tried once more after the connection dropped.</summary>
internal static class MailRetry
{
    /// <summary>
    /// Once only, and only for a dropped connection (common after the PC sleeps). A job that adds something to
    /// the server is tried again only if it never started, since it may have landed before the line dropped.
    /// </summary>
    public static bool TryAgain(int attempt, bool jobStarted, bool repeatable, Exception error) =>
        attempt == 0 && (repeatable || !jobStarted)
        && error is IOException or SocketException or ServiceNotConnectedException or ImapProtocolException;
}
