using Webignite.Core.Documents;
using Webignite.Core.Xero;
using Webignite.Data;
using Webignite.Xero;

namespace Webignite.Services;

/// <summary>What bringing in Xero's quotes and invoices did, or why it couldn't.</summary>
public sealed record XeroDocumentNews(int Added, int Refreshed, string? Problem = null)
{
    public string Summary => (Added, Refreshed) switch
    {
        (0, 0) => "Nothing new from Xero.",
        (_, 0) => Added == 1 ? "1 quote or invoice made in Xero came in." : $"{Added} quotes and invoices made in Xero came in.",
        (0, _) => Refreshed == 1 ? "1 changed in Xero was brought up to date." : $"{Refreshed} changed in Xero were brought up to date.",
        _ => $"{Added} came in from Xero and {Refreshed} were brought up to date.",
    };
}

/// <summary>Where bringing in Xero's quotes and invoices is up to (setting <c>xero-documents</c>).</summary>
public sealed record XeroDocumentsState(DateTimeOffset? LastRun = null);

/// <summary>
/// Brings in quotes and invoices made in Xero itself, when the app starts and every few hours, so lists, Money,
/// Reports and reminders count everything. The first look reads two years; later ones only what Xero changed
/// since. Xero is only read; Webignite's own documents aren't touched (the payment check keeps them current).
/// </summary>
public sealed class XeroDocumentWatch(LocalStore store, Func<CancellationToken, DateOnly, DateTimeOffset?, Task<IReadOnlyList<XeroSalesDocument>>?> read,
    TimeProvider? clock = null)
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(4);
    private const string StateName = "xero-documents";

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private bool running;

    public XeroDocumentWatch(LocalStore store, Func<XeroApi?> xero, TimeProvider? clock = null)
        : this(store, (cancel, today, since) => xero() is { } api ? new XeroDocumentReader(api).ReadAsync(today, since, cancel) : null, clock)
    {
    }

    public DateTimeOffset? LastTry { get; private set; }

    public bool Due(DateTimeOffset now) => LastTry is not { } last || now - last >= Every;

    public XeroDocumentsState State => store.GetSetting<XeroDocumentsState>(StateName) ?? new XeroDocumentsState();

    /// <summary>Reads Xero and brings in what's new or changed. With <paramref name="fromScratch"/>, looks back the full two years again.</summary>
    public async Task<XeroDocumentNews> RunAsync(bool fromScratch = false, CancellationToken cancel = default)
    {
        if (running)
            return new XeroDocumentNews(0, 0);
        var started = clock.GetUtcNow();
        var since = fromScratch ? null : State.LastRun;
        if (read(cancel, DateOnly.FromDateTime(clock.GetLocalNow().DateTime), since) is not { } reading)
            return new XeroDocumentNews(0, 0, "Connect Xero in Settings first.");
        running = true;
        LastTry = started;
        try
        {
            var rows = await reading;
            var plan = XeroDocuments.Plan(store.List(), rows, started);
            var added = 0;
            foreach (var document in plan.ToAdd)
            {
                // Looked for again, in case Webignite linked the same one while Xero was being read.
                if (store.List(document.Document.Kind).Any(d => string.Equals(d.Xero.Id, document.Xero.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                store.Save(document);
                added++;
            }
            var refreshed = 0;
            foreach (var fresh in plan.ToRefresh)
            {
                // Applied to the record as stored now; anything chased or emailed meanwhile is kept.
                if (store.Update(fresh.Id, current => current.Document.Origin == DocumentOrigin.Xero
                        ? fresh with
                        {
                            Delivery = current.Delivery, Settled = fresh.Settled ?? current.Settled, Created = current.Created,
                            CreditNotes = current.CreditNotes, Attachments = current.Attachments, FromQuote = current.FromQuote,
                        }
                        : null) is not null)
                    refreshed++;
            }
            // Webignite's own deleted or voided in Xero: settled here, so they leave To approve and the lists show why.
            foreach (var (id, how) in plan.Gone)
            {
                if (store.Update(id, current => current.Settled is null
                        ? current with { Settled = new SettledState { On = started, As = how }, Xero = current.Xero with { Status = how == Settlement.Voided ? XeroStatuses.Voided : XeroStatuses.Deleted } }
                        : null) is not null)
                    refreshed++;
            }
            store.SetSetting(StateName, new XeroDocumentsState(started));
            return new XeroDocumentNews(added, refreshed);
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new XeroDocumentNews(0, 0, "Couldn't read quotes and invoices from Xero: " + e.Message);
        }
        finally
        {
            running = false;
        }
    }
}
