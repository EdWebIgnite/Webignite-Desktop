using Webignite.Core.Documents;
using Webignite.Xero;

namespace Webignite.Desktop;

/// <summary>
/// Reads payments and statuses from Xero for every open invoice and quote, saves what changed, ticks off
/// "chase" to-dos for invoices that are now paid, and returns payments to announce. Xero isn't changed.
/// </summary>
public static class PaymentWatch
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(30);

    private static bool running;

    /// <summary>When Xero was last read successfully.</summary>
    public static DateTimeOffset? LastRun { get; private set; }

    public static DateTimeOffset? LastTry { get; private set; }

    public static bool Due(DateTimeOffset now) => LastTry is not { } last || now - last >= Every;

    /// <summary>Returns the payments that came in, or a problem to show when asked by hand.</summary>
    public static async Task<(IReadOnlyList<PaymentNews> News, string? Problem)> RunAsync(AppServices services, CancellationToken cancel = default)
    {
        if (running)
            return ([], null);
        if (services.XeroApi is not { } api)
            return ([], "Connect Xero in Settings first.");
        running = true;
        LastTry = DateTimeOffset.Now;
        try
        {
            var open = services.Store.List().Where(Receivables.NeedsChecking).ToList();
            var standings = new XeroStandings(api);
            var invoices = await standings.InvoicesAsync(open.Where(d => d.Document.Kind == DocumentKind.Invoice).Select(d => d.XeroId!), cancel);
            var quotes = await standings.QuotesAsync(open.Where(d => d.Document.Kind == DocumentKind.Quote).Select(d => d.XeroId!), cancel);

            var news = new List<PaymentNews>();
            var now = DateTimeOffset.UtcNow;
            foreach (var doc in open)
            {
                var found = doc.Document.Kind == DocumentKind.Invoice ? invoices : quotes;
                if (!found.TryGetValue(doc.XeroId!, out var standing))
                    continue;
                // Read again: the document may have been changed while Xero was being read.
                var current = services.Store.Get(doc.Id) ?? doc;
                var (changed, payment) = Receivables.Apply(current, standing, now);
                if (changed is null)
                    continue;
                services.Store.Save(changed);
                if (payment is not null)
                    news.Add(payment);
                if (changed.Settled is not null)
                    TickOffChasing(services, changed);
            }
            LastRun = DateTimeOffset.Now;
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

    // A paid invoice or settled quote doesn't need its automatic "chase" or "follow up" to-do any more.
    private static void TickOffChasing(AppServices services, StoredDocument stored)
    {
        foreach (var task in services.Store.ListTasks().Where(t => !t.Done && t.DocumentId == stored.Id
            && t.AutoKey is { } key && (key.StartsWith("invoice-overdue:", StringComparison.Ordinal) || key.StartsWith("quote-follow-up:", StringComparison.Ordinal))))
            services.Store.SaveTask(task with { Done = true, DoneAt = DateTimeOffset.UtcNow });
    }
}
