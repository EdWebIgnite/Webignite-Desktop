using System.Net;
using System.Net.Sockets;
using System.Text;
using Webignite.Core.Mail;
using Webignite.Data;
using Webignite.Mail;

namespace Webignite.Tests;

/// <summary>New mail shows as it arrives: the watch hears the server announce it while waiting on the inbox.</summary>
public sealed class MailWatchTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_watch_hears_new_mail_the_moment_the_server_announces_it()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(() => ServeOneAsync(listener, Cancel), Cancel);

        var account = new MailAccount { Id = "eddie", DisplayName = "Eddie", Address = "eddie@webignite.co.za", ImapHost = "127.0.0.1", ImapPort = port, SmtpHost = "127.0.0.1" };
        var secrets = new MemorySecretStore();
        secrets.Write(MailService.SecretName(account), "karoo");
        var heard = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var watch = new MailWatch(secrets) { NewMail = id => heard.TrySetResult(id) })
        {
            watch.Keep([account]);
            var first = await Task.WhenAny(heard.Task, Task.Delay(TimeSpan.FromSeconds(15), Cancel));
            Assert.Same(heard.Task, first);
            Assert.Equal("eddie", await heard.Task);
        }
        listener.Stop();
        await Task.WhenAny(server, Task.Delay(TimeSpan.FromSeconds(3), Cancel));
    }

    [Fact]
    public void After_a_failure_it_waits_longer_each_time_up_to_ten_minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), MailWatchRules.RetryAfter(1));
        Assert.Equal(TimeSpan.FromSeconds(60), MailWatchRules.RetryAfter(2));
        Assert.Equal(TimeSpan.FromMinutes(10), MailWatchRules.RetryAfter(20));
        Assert.True(MailWatchRules.WaitFor < TimeSpan.FromMinutes(29));
    }

    // Just enough of a mail server: sign in, the inbox, and an IDLE that announces a second mail shortly after it starts.
    private static async Task ServeOneAsync(TcpListener listener, CancellationToken cancel)
    {
        using var client = await listener.AcceptTcpClientAsync(cancel);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("* OK [CAPABILITY IMAP4rev1 IDLE] Ready");
        var idles = 0;
        while (await reader.ReadLineAsync(cancel) is { } line)
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space < 0)
                continue;
            var tag = line[..space];
            var command = line[(space + 1)..];
            var verb = command.Split(' ')[0].ToUpperInvariant();
            switch (verb)
            {
                case "CAPABILITY":
                    await writer.WriteLineAsync("* CAPABILITY IMAP4rev1 IDLE");
                    await writer.WriteLineAsync($"{tag} OK done");
                    break;
                case "LOGIN":
                    await writer.WriteLineAsync($"{tag} OK [CAPABILITY IMAP4rev1 IDLE] Signed in");
                    break;
                case "LIST":
                    if (command.Contains("\"\" \"\"", StringComparison.Ordinal))
                        await writer.WriteLineAsync("* LIST (\\Noselect) \"/\" \"\"");
                    else
                        await writer.WriteLineAsync("* LIST (\\HasNoChildren) \"/\" INBOX");
                    await writer.WriteLineAsync($"{tag} OK done");
                    break;
                case "EXAMINE" or "SELECT":
                    await writer.WriteLineAsync("* FLAGS (\\Seen)");
                    await writer.WriteLineAsync("* 1 EXISTS");
                    await writer.WriteLineAsync("* 0 RECENT");
                    await writer.WriteLineAsync("* OK [UIDVALIDITY 1] ok");
                    await writer.WriteLineAsync("* OK [UIDNEXT 2] ok");
                    await writer.WriteLineAsync($"{tag} OK [READ-ONLY] done");
                    break;
                case "IDLE":
                    await writer.WriteLineAsync("+ idling");
                    if (idles++ == 0)
                    {
                        await Task.Delay(200, cancel);
                        await writer.WriteLineAsync("* 2 EXISTS");
                    }
                    while (await reader.ReadLineAsync(cancel) is { } done && !done.Equals("DONE", StringComparison.OrdinalIgnoreCase))
                    {
                    }
                    await writer.WriteLineAsync($"{tag} OK idle done");
                    break;
                case "LOGOUT":
                    await writer.WriteLineAsync("* BYE");
                    await writer.WriteLineAsync($"{tag} OK done");
                    return;
                default:
                    await writer.WriteLineAsync($"{tag} OK done");
                    break;
            }
        }
    }
}
