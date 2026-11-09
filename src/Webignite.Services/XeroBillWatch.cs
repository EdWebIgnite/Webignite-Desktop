using Webignite.Core.Finance;
using Webignite.Data;
using Webignite.Xero;

namespace Webignite.Services;

/// <summary>
/// Reads the bills in Xero that are still to approve or pay, when the app starts and every few hours, so Money and
/// Overview show what Webignite owes. Xero is only read. Kept in setting <c>xero-bills</c>.
/// </summary>
public sealed class XeroBillWatch(LocalStore store, Func<CancellationToken, Task<IReadOnlyList<XeroBill>>?> read, TimeProvider? clock = null)
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(4);
    private const string StateName = "xero-bills";

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private bool running;

    public XeroBillWatch(LocalStore store, Func<XeroApi?> xero, TimeProvider? clock = null)
        : this(store, cancel => xero() is { } api ? api.OpenBillsAsync(cancel) : null, clock)
    {
    }

    public DateTimeOffset? LastTry { get; private set; }

    public bool Due(DateTimeOffset now) => LastTry is not { } last || now - last >= Every;

    public BillsState State => store.GetSetting<BillsState>(StateName) ?? new BillsState();

    /// <summary>Reads Xero's open bills. Returns a problem to show, or null when they were read.</summary>
    public async Task<string?> RunAsync(CancellationToken cancel = default)
    {
        if (running)
            return null;
        if (read(cancel) is not { } reading)
            return "Connect Xero in Settings first.";
        running = true;
        LastTry = clock.GetUtcNow();
        try
        {
            var bills = await reading;
            store.SetSetting(StateName, new BillsState { Read = clock.GetUtcNow(), Open = bills });
            return null;
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return "Couldn't read the bills from Xero: " + e.Message;
        }
        finally
        {
            running = false;
        }
    }
}
