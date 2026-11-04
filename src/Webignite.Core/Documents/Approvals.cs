using Webignite.Core.Xero;

namespace Webignite.Core.Documents;

/// <summary>Why a document is on the To approve list, most urgent first.</summary>
public enum WaitingKind
{
    /// <summary>Something went wrong talking to Xero and needs a look.</summary>
    XeroProblem,

    /// <summary>Claude or ChatGPT prepared it; nothing has gone to Xero.</summary>
    FromAssistant,

    /// <summary>A draft in Xero, not approved or emailed yet.</summary>
    XeroDraft,

    /// <summary>Approved in Xero but not emailed to the customer.</summary>
    NotEmailed,
}

public sealed record WaitingItem(StoredDocument Stored, WaitingKind Kind, string Reason);

public static class Approvals
{
    /// <summary>Everything that needs Eddie, most urgent first and oldest first within each kind.</summary>
    public static IReadOnlyList<WaitingItem> Waiting(IEnumerable<StoredDocument> documents) =>
        documents.Select(Why).OfType<WaitingItem>()
            .OrderBy(w => w.Kind)
            .ThenBy(w => w.Stored.Created)
            .ToList();

    /// <summary>
    /// Whether it can be thrown away from To approve: only what Claude or ChatGPT prepared and never reached Xero.
    /// Anything in Xero is dealt with in Xero.
    /// </summary>
    public static bool CanReject(WaitingItem item) => item.Kind == WaitingKind.FromAssistant && item.Stored.Xero.Id is null;

    public static WaitingItem? Why(StoredDocument s)
    {
        var what = s.Document.Kind == DocumentKind.Quote ? "quote" : "invoice";
        var by = s.Source == DocumentSource.ChatGpt ? "ChatGPT" : "Claude";
        return s switch
        {
            { Xero.State: XeroWriteState.Writing or XeroWriteState.Uncertain } =>
                new(s, WaitingKind.XeroProblem, "It isn't clear whether Xero got it. Open it and use Check Xero."),
            { Xero.State: XeroWriteState.Mismatch } =>
                new(s, WaitingKind.XeroProblem, "Xero's copy differs from this one. Open it to see how."),
            { Xero.State: XeroWriteState.None, Status: DocumentStatus.AwaitingApproval } =>
                new(s, WaitingKind.FromAssistant, $"{by} prepared this {what}. Check it, then create it in Xero or reject it."),
            { Xero.State: XeroWriteState.Verified, Delivery: { Emailed: null, Approved: null } } =>
                new(s, WaitingKind.XeroDraft, s.Document.Kind == DocumentKind.Quote
                    ? "Draft quote in Xero, not emailed yet."
                    : "Draft invoice in Xero, not approved or emailed yet."),
            { Xero.State: XeroWriteState.Verified, Delivery: { Emailed: null, Approved: not null } } =>
                new(s, WaitingKind.NotEmailed, "Approved in Xero but not emailed to the customer."),
            _ => null,
        };
    }
}
