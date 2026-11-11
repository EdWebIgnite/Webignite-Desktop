using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Webignite.Core.Mail;
using Webignite.Data;

namespace Webignite.Mail;

/// <summary>
/// Watches each mailbox's inbox on a connection of its own (IMAP IDLE), so new mail shows within seconds rather than
/// at the next check. It only listens: when the server says the inbox changed, <see cref="NewMail"/> is told, and the
/// app runs its usual check. A server without IDLE, or a mailbox without a saved password, isn't watched.
/// </summary>
public sealed class MailWatch(ISecretStore secrets) : IAsyncDisposable
{
    private readonly Dictionary<string, (CancellationTokenSource Stop, Task Running)> watching = [];
    private readonly Lock gate = new();

    /// <summary>A mailbox's inbox has new mail (or mail went away); told on a background thread, with the mailbox's id.</summary>
    public Action<string>? NewMail { get; set; }

    /// <summary>Watches exactly these mailboxes: new ones start, ones no longer there stop.</summary>
    public void Keep(IReadOnlyList<MailAccount> accounts)
    {
        lock (gate)
        {
            var wanted = accounts.Where(a => !string.IsNullOrEmpty(secrets.Read(MailService.SecretName(a)))).ToDictionary(a => a.Id);
            foreach (var id in watching.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
            {
                watching[id].Stop.Cancel();
                watching.Remove(id);
            }
            foreach (var (id, account) in wanted)
            {
                if (watching.ContainsKey(id))
                    continue;
                var stop = new CancellationTokenSource();
                watching[id] = (stop, Task.Run(() => Watch(account, stop.Token)));
            }
        }
    }

    private async Task Watch(MailAccount account, CancellationToken stop)
    {
        var failures = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (secrets.Read(MailService.SecretName(account)) is not { Length: > 0 } password)
                    return;
                using var client = new ImapClient { Timeout = 60_000 };
                await client.ConnectAsync(account.ImapHost, account.ImapPort, SecureSocketOptions.Auto, stop);
                await client.AuthenticateAsync(account.SignInName, password, stop);
                if (!client.Capabilities.HasFlag(ImapCapabilities.Idle))
                {
                    await client.DisconnectAsync(true, stop);
                    return; // the check every few minutes covers this server
                }
                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, stop);
                failures = 0;
                var changed = false;
                CancellationTokenSource? waiting = null;
                inbox.CountChanged += (_, _) =>
                {
                    changed = true;
                    waiting?.Cancel(); // ends this wait at once, so the app hears straight away
                };
                while (!stop.IsCancellationRequested)
                {
                    using (waiting = CancellationTokenSource.CreateLinkedTokenSource(stop))
                    {
                        waiting.CancelAfter(MailWatchRules.WaitFor);
                        await client.IdleAsync(waiting.Token, stop);
                    }
                    waiting = null;
                    if (changed)
                    {
                        changed = false;
                        NewMail?.Invoke(account.Id);
                    }
                }
                await client.DisconnectAsync(true, CancellationToken.None);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Logged the first time only: while the internet is down it would fail every few minutes.
                if (failures++ == 0)
                    AppLog.Write($"Watching {account.Address} for new mail", e);
                try
                {
                    await Task.Delay(MailWatchRules.RetryAfter(failures), stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Task> running;
        lock (gate)
        {
            foreach (var (stop, _) in watching.Values)
                stop.Cancel();
            running = [.. watching.Values.Select(w => w.Running)];
            watching.Clear();
        }
        await Task.WhenAny(Task.WhenAll(running), Task.Delay(TimeSpan.FromSeconds(3)));
    }
}
