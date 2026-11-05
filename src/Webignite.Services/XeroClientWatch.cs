using Webignite.Core.Clients;
using Webignite.Data;
using Webignite.Xero;

namespace Webignite.Services;

/// <summary>What bringing Xero's customers in did, or why it couldn't.</summary>
public sealed record XeroClientNews(int Added, int Filled, string? Problem = null)
{
    public string Summary => XeroClients.Summary(Added, Filled);
}

/// <summary>
/// Brings Xero's customers in as clients when the app starts and every few hours: new customers become clients
/// and blank details are filled in. Each change is made to the client as stored now, so nothing typed on a
/// client page is lost. Xero is only read.
/// </summary>
public sealed class XeroClientWatch(LocalStore store, Func<CancellationToken, Task<IReadOnlyList<XeroCustomer>>?> read, TimeProvider? clock = null)
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(4);

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private bool running;

    /// <summary>Reads Xero through its API, or nothing while Xero isn't connected.</summary>
    public XeroClientWatch(LocalStore store, Func<XeroApi?> xero, TimeProvider? clock = null)
        : this(store, cancel => xero() is { } api ? api.CustomersAsync(cancel) : null, clock)
    {
    }

    public DateTimeOffset? LastTry { get; private set; }

    public bool Due(DateTimeOffset now) => LastTry is not { } last || now - last >= Every;

    public async Task<XeroClientNews> RunAsync(CancellationToken cancel = default)
    {
        if (running)
            return new XeroClientNews(0, 0);
        if (read(cancel) is not { } reading)
            return new XeroClientNews(0, 0, "Connect Xero in Settings first.");
        running = true;
        LastTry = clock.GetUtcNow();
        try
        {
            var contacts = await reading;
            var plan = XeroClients.Plan(store.ListClients(), contacts);
            var now = clock.GetUtcNow();
            var added = 0;
            foreach (var contact in plan.ToAdd)
            {
                // Looked for again, in case the same client was added by hand while Xero was being read.
                if (ClientDirectory.Find(store.ListClients(), contact.Name) is not null)
                    continue;
                store.SaveClient(XeroClients.New(contact, Client.NewId(), now));
                added++;
            }
            var filled = 0;
            foreach (var (id, contact) in plan.ToFill)
            {
                if (store.UpdateClient(id, current => XeroClients.Fill(current, contact)) is not null)
                    filled++;
            }
            return new XeroClientNews(added, filled);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new XeroClientNews(0, 0, "Couldn't read clients from Xero: " + e.Message);
        }
        finally
        {
            running = false;
        }
    }
}
