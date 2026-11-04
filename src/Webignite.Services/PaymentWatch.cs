using Webignite.Core.Documents;
using Webignite.Data;
using Webignite.Xero;

namespace Webignite.Services;

/// <summary>
/// Reads payments and statuses from Xero for every open invoice and quote, saves what changed, ticks off
/// "chase" to-dos for invoices that are now paid, and returns payments to announce. Xero isn't changed.
/// </summary>
public sealed class PaymentWatch(LocalStore store, Func<XeroApi?> xero, TimeProvider? clock = null)
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(30);

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private bool running;

    /// <summary>When Xero was last read successfully.</summary>
    public DateTimeOffset? LastRun { get; private set; }

    public DateTimeOffset? LastTry { get; private set; }

    public bool Due(DateTimeOffset now) => LastTry is not { } last || now - last >= Every;

    /// <summary>Returns the payments that came in, or a problem to show when asked by hand.</summary>
    public async Task<(IReadOnlyList<PaymentNews> News, string? Problem)> RunAsync(CancellationToken cancel = default)
    {
        if (running)
            return ([], null);
        if (xero() is not { } api)
            return ([], "Connect Xero in Settings first.");
        running = true;
        LastTry = clock.GetLocalNow();
        try
        {
            var open = store.List().Where(Receivables.NeedsChecking).ToList();
            var standings = new XeroStandings(api);
            var invoices = await standings.InvoicesAsync(open.Where(d => d.Document.Kind == DocumentKind.Invoice).Select(d => d.XeroId!), cancel);
            var quotes = await standings.QuotesAsync(open.Where(d => d.Document.Kind == DocumentKind.Quote).Select(d => d.XeroId!), cancel);

            var news = new List<PaymentNews>();
            var now = clock.GetUtcNow();
            foreach (var doc in open)
            {
                var found = doc.Document.Kind == DocumentKind.Invoice ? invoices : quotes;
                if (!found.TryGetValue(doc.XeroId!, out var standing))
                    continue;
                // Applied to the document as it is now: it may have been changed while Xero was being read.
                StoredDocument? changed = null;
                PaymentNews? payment = null;
                store.Update(doc.Id, current =>
                {
                    (changed, payment) = Receivables.Apply(current, standing, now);
                    return changed;
                });
                if (changed is null)
                    continue;
                if (payment is not null)
                    news.Add(payment);
                if (changed.Settled is not null)
                    store.TickOffChasing(changed.Id, now);
            }
            LastRun = clock.GetLocalNow();
            return (news, null);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return ([], "Couldn't read payments from Xero: " + e.Message);
        }
        finally
        {
            running = false;
        }
    }
}
